using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace ARV.Tests
{
    [Transaction(TransactionMode.Manual)]
    public class NativeChecks : IExternalCommand
    {
        private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), @"..\.."));
        private readonly List<string> log = new List<string>();
        private Document doc;
        private ExternalCommandData data;
        private Room room;
        private WallType wallType;
        private List<Wall> walls;
        private ViewPlan plan;
        private ViewSheet sheet;
        private List<ViewDrafting> views;
        private ElementId subCategory;
        private int failed;
        private string report => Path.Combine(Root, "artifacts", "native-checks-report.txt");
        private void Write(string text) { log.Add(text); File.WriteAllLines(report, log); }
        private static void Assert(bool value, string text) { if (!value) throw new InvalidOperationException(text); }
        private static Assembly Load(string project) => Assembly.LoadFrom(Path.Combine(Root, "src", project, "bin", "Release", project + ".dll"));
        private void Case(string title, Action action)
        {
            int before = new FilteredElementCollector(doc).ToElementIds().Count;
            using (var group = new TransactionGroup(doc, "ARV check " + title))
            {
                group.Start();
                try { action(); Write("PASS " + title); }
                catch (Exception ex) { failed++; Write("FAIL " + title + ": " + (ex.InnerException ?? ex).Message); }
                finally { if (group.GetStatus() == TransactionStatus.Started) group.RollBack(); }
            }
            Assert(before == new FilteredElementCollector(doc).ToElementIds().Count, "Case rollback element count: " + title);
        }
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            data = commandData; doc = data.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) return Result.Cancelled;
            int before = new FilteredElementCollector(doc).ToElementIds().Count;
            var selection = data.Application.ActiveUIDocument.Selection.GetElementIds();
            Write("ARV 1.1.0 native checks; Revit " + data.Application.Application.VersionNumber);
            Write("Document: " + doc.Title + "; Workshared=" + doc.IsWorkshared + "; Original elements=" + before);
            try
            {
                using (var all = new TransactionGroup(doc, "ARV verification — always rollback"))
                {
                    all.Start();
                    try
                    {
                        Fixture();
                        Case("finishing parameter reading and preserved skirting calculation", Finishing);
                        Case("rough finishing native area and parameters", Rough);
                        Case("crop preserves Z", Crop);
                        Case("hide category preserves assigned templates", () => Hide("HideCategory", new ElementId(BuiltInCategory.OST_Walls)));
                        Case("hide subcategory preserves assigned templates", () => Hide("HideSubCategory", subCategory));
                        Case("WPF dialogs initialize and expose selections", Windows);
                        Case("site availability", Site);
                        Case("copy views actual command", CopyUi);
                        Case("placement actual command alphabetical order", PlacementUi);
                        Case("numbering actual command", NumberingUi);
                        Case("create sheets actual command", SheetsUi);
                        Case("worksets actual command guard", WorksetsUi);
                    }
                    finally { if (all.GetStatus() == TransactionStatus.Started) all.RollBack(); }
                }
                data.Application.ActiveUIDocument.Selection.SetElementIds(selection);
                int after = new FilteredElementCollector(doc).ToElementIds().Count;
                Assert(before == after, "Final element count mismatch");
                Write("ROLLBACK_OK: " + before + " = " + after + "; original selection restored");
                Write("Failures=" + failed);
                TaskDialog.Show("ARV native checks", "Проверки завершены. Ошибок: " + failed + ".\nВсе тестовые изменения отменены.\n" + report);
                return failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex) { message = (ex.InnerException ?? ex).ToString(); Write("ABORT " + message); data.Application.ActiveUIDocument.Selection.SetElementIds(selection); return Result.Failed; }
        }
        private void Fixture()
        {
            using (var tx = new Transaction(doc, "ARV test fixture"))
            {
                tx.Start();
                var level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).First();
                wallType = (WallType)new FilteredElementCollector(doc).OfClass(typeof(WallType)).Cast<WallType>().First(t => t.Kind == WallKind.Basic).Duplicate("ARV_Test_" + Guid.NewGuid().ToString("N"));
                var points = new[] { new XYZ(200,200,level.Elevation), new XYZ(220,200,level.Elevation), new XYZ(220,220,level.Elevation), new XYZ(200,220,level.Elevation) };
                walls = Enumerable.Range(0,4).Select(i => Wall.Create(doc, Line.CreateBound(points[i], points[(i+1)%4]), wallType.Id, level.Id, 10, 0, false, false)).ToList();
                room = doc.Create.NewRoom(level, new UV(210,210));
                room.Number = "ARV_NATIVE_" + Guid.NewGuid().ToString("N").Substring(0,8);
                var planType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.FloorPlan);
                plan = ViewPlan.Create(doc, planType.Id, level.Id);
                var draftType = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(t => t.ViewFamily == ViewFamily.Drafting);
                string suffix = Guid.NewGuid().ToString("N").Substring(0,6);
                views = new[] { "Я_", "А_", "Б_" }.Select(name => { var view = ViewDrafting.Create(doc, draftType.Id); view.Name = name + suffix; return view; }).ToList();
                sheet = ViewSheet.Create(doc, ElementId.InvalidElementId);
                sheet.Name = "ARV_NATIVE_TEST"; sheet.SheetNumber = "ARV_NATIVE_" + suffix;
                var templateId = plan.CreateViewTemplate().Id;
                plan.ViewTemplateId = templateId;
                subCategory = doc.Settings.Categories.NewSubcategory(doc.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel), "ARV_TEST_" + suffix).Id;
                BindParameters();
                wallType.LookupParameter("Группа модели").Set("ARV_NATIVE");
                wallType.LookupParameter("ВО_Описание").Set("ARV тестовая отделка");
                foreach (var wall in walls) wall.LookupParameter("ADSK_Группирование").Set(room.Number);
                room.LookupParameter("ADSK_Длина плинтуса").Set(1.0);
                doc.Regenerate();
                Assert(room.Area > 0, "Fixture room is not enclosed");
                Assert(tx.Commit() == TransactionStatus.Committed, "Fixture commit");
                Write("Fixture: room=" + room.Id + "; area=" + room.Area + "; titleblock types=" + new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().ToElementIds().Count);
            }
        }
        private void BindParameters()
        {
            var app = data.Application.Application;
            string original = app.SharedParametersFilename;
            string path = Path.Combine(Root, "artifacts", "native-shared-parameters.txt");
            File.WriteAllText(path, "# This is a Revit shared parameter file.\r\n*META\tVERSION\tMINVERSION\r\nMETA\t2\t1\r\n*GROUP\tID\tNAME\r\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\tHIDEWHENNOVALUE\r\n");
            try
            {
                app.SharedParametersFilename = path;
                var file = app.OpenSharedParameterFile();
                var group = file.Groups.Create("ARV native test");
                var names = new Dictionary<string, ForgeTypeId>();
                for (int i=1; i<=5; i++) { names[$"ВО_Стены_Черновая_{i}_Материал"] = SpecTypeId.String.Text; names[$"ВО_Стены_Черновая_{i}_Площадь"] = SpecTypeId.Area; }
                foreach (string name in new[] { "ВО_СписокНомеровСтены_Черновая", "ВО_Стены_Финишная_Материал", "ВО_Стены_Финишная_Площадь", "ВО_Потолки_Материал", "ВО_Потолки_Площадь", "ВО_СписокНомеровСтены", "Тип плинтуса" }) names[name] = SpecTypeId.String.Text;
                names["ADSK_Длина плинтуса"] = SpecTypeId.Length;
                foreach (var name in names) AddParameter(group, name.Key, name.Value, BuiltInCategory.OST_Rooms, false, room);
                AddParameter(group, "ADSK_Группирование", SpecTypeId.String.Text, BuiltInCategory.OST_Walls, false, walls[0]);
                AddParameter(group, "Группа модели", SpecTypeId.String.Text, BuiltInCategory.OST_Walls, true, wallType);
                AddParameter(group, "ВО_Описание", SpecTypeId.String.Text, BuiltInCategory.OST_Walls, true, wallType);
                doc.Regenerate();
            }
            finally { app.SharedParametersFilename = original; }
        }
        private void AddParameter(DefinitionGroup group, string name, ForgeTypeId type, BuiltInCategory category, bool typeBinding, Element target)
        {
            if (target.LookupParameter(name) != null) return;
            var definition = group.Definitions.Create(new ExternalDefinitionCreationOptions(name, type));
            var categories = data.Application.Application.Create.NewCategorySet(); categories.Insert(doc.Settings.Categories.get_Item(category));
            Binding binding = typeBinding ? (Binding)data.Application.Application.Create.NewTypeBinding(categories) : data.Application.Application.Create.NewInstanceBinding(categories);
            Assert(doc.ParameterBindings.Insert(definition, binding, BuiltInParameterGroup.PG_DATA), "Bind " + name);
            doc.Regenerate();
        }
        private void Finishing()
        {
            var assembly = Load("FinishingSchedulePlugin");
            var type = assembly.GetType("FinishingSchedulePlugin.Command");
            var command = Activator.CreateInstance(type);
            using(var tx = new Transaction(doc,"Test finishing"))
            {
                tx.Start();
                var method = type.GetMethod("ProcessRooms", BindingFlags.Instance | BindingFlags.NonPublic);
                method.Invoke(command, new object[] {doc,"ARV_NATIVE","Нет",10.0});
                Assert(!string.IsNullOrEmpty(room.LookupParameter("ВО_Стены_Финишная_Материал").AsString()), "No finishing material text");
                Assert(Math.Abs(room.LookupParameter("ADSK_Длина плинтуса").AsDouble() - 1.1) < 1e-8, "Skirting existing formula changed");
                method.Invoke(command, new object[] {doc,"ARV_NATIVE","Нет",10.0});
                Assert(Math.Abs(room.LookupParameter("ADSK_Длина плинтуса").AsDouble() - 1.21) < 1e-8, "Requested repeat behavior not preserved");
                Assert(tx.Commit() == TransactionStatus.Committed, "Finishing commit");
            }
        }
        private void Rough()
        {
            var type = Load("WallStartSchedule").GetType("WallStartSchedule.WallStartCalculator");
            var calculator = Activator.CreateInstance(type, doc);
            var result = type.GetMethod("CalculateAndWrite").Invoke(calculator, new object[] {new Dictionary<int,ElementId>{{1,wallType.Id}},10.0});
            Assert((bool)result.GetType().GetProperty("Success").GetValue(result), (string)result.GetType().GetProperty("ErrorMessage").GetValue(result));
            double expected = walls.Sum(w => w.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED).AsDouble()) * 1.1;
            Assert(Math.Abs(room.LookupParameter("ВО_Стены_Черновая_1_Площадь").AsDouble() - expected) < 1e-8, "Wrong native rough area");
            Assert(room.LookupParameter("ВО_Стены_Черновая_1_Материал").AsString() == "ARV тестовая отделка", "Wrong rough material");
        }
        private void Crop()
        {
            using(var tx = new Transaction(doc,"Test crop"))
            {
                tx.Start(); plan.ViewTemplateId = ElementId.InvalidElementId;
                var before = plan.CropBox;
                var method = Load("TightCropPlugin").GetType("AdjustCropToBreakLines.AdjustCropToBreakLinesCommand").GetMethod("AdjustCropBox");
                method.Invoke(null,new object[]{plan,50.0,false,true}); doc.Regenerate();
                Assert(Math.Abs(plan.CropBox.Min.Z-before.Min.Z)<1e-8 && Math.Abs(plan.CropBox.Max.Z-before.Max.Z)<1e-8,"Crop changed Z");
                Assert(plan.CropBoxActive,"Crop not activated");
                tx.RollBack();
            }
        }
        private void Hide(string project, ElementId category)
        {
            var template = plan.ViewTemplateId;
            Load(project).GetType("ARV.Common.HideCategoryService").GetMethod("Hide").Invoke(null,new object[]{doc,category});
            Assert(plan.ViewTemplateId == template,"Template detached");
            Assert(((View)doc.GetElement(template)).GetCategoryHidden(category),"Template category not hidden");
        }
        private void Windows()
        {
            foreach(var item in new[]{Tuple.Create("HideCategory","HideCategory.UserWindRoom"),Tuple.Create("HideSubCategory","HideCategory.UserWindRoom")})
            {
                var window=(Window)Activator.CreateInstance(Load(item.Item1).GetType(item.Item2),data.Application);
                window.Measure(new System.Windows.Size(600,600)); window.Close();
            }
            var numbering=(Window)Activator.CreateInstance(Load("ViewNumberingPlugin").GetType("ViewNumberingPlugin.NumberingDialog"));
            Assert(((System.Windows.Controls.Grid)numbering.Content).RowDefinitions.Count==5,"Numbering dialog rows"); numbering.Close();
            var copy=(Window)Activator.CreateInstance(Load("CopyViewsWithRename").GetType("CopyViewsWithRename.CopyViewsWindow"),views.Cast<View>().ToList()); copy.Close();
        }
        private void Site()
        {
            var availability=(IExternalCommandAvailability)Activator.CreateInstance(Load("BuildinOpener").GetType("BuildinOpener.SiteAvailability"));
            Assert(availability.IsCommandAvailable(data.Application,null),"Site unavailable");
            Write("NOTE Site command browser navigation not invoked; UI availability checked.");
        }
        private Result RunCommand(string project,string type,ICollection<ElementId> selected)
        {
            data.Application.ActiveUIDocument.Selection.SetElementIds(selected);
            string error="";
            var command=(IExternalCommand)Activator.CreateInstance(Load(project).GetType(type));
            Write("WAITING_UI "+project);
            var result=command.Execute(data,ref error,new ElementSet());
            Write("COMMAND_RESULT "+project+" "+result+" "+error);
            return result;
        }
        private void CopyUi()
        {
            int before=new FilteredElementCollector(doc).OfClass(typeof(View)).ToElementIds().Count;
            Assert(RunCommand("CopyViewsWithRename","CopyViewsWithRename.CopyViewsCommand",views.Select(v=>v.Id).ToList())==Result.Succeeded,"Copy command failed");
            Assert(new FilteredElementCollector(doc).OfClass(typeof(View)).ToElementIds().Count==before+3,"Expected 3 view copies");
        }
        private void PlacementUi()
        {
            Assert(RunCommand("PlaceViewsOnSheet","PlaceViewsOnSheet.PlaceViewsCommand",views.Select(v=>v.Id).ToList())==Result.Succeeded,"Placement command failed");
            var added=new FilteredElementCollector(doc).OfClass(typeof(Viewport)).Cast<Viewport>().Where(v=>views.Any(d=>d.Id==v.ViewId)).ToList();
            Assert(added.Count==3,"Expected 3 placements");
            var expected=views.OrderBy(v=>v.Name,StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("ru-RU"),true)).ToList();
            var actual=added.OrderByDescending(v=>v.GetBoxCenter().Y).ThenBy(v=>v.GetBoxCenter().X).Select(v=>v.ViewId).ToList();
            Assert(actual.SequenceEqual(expected.Select(v=>v.Id)),"Placement alphabetical order mismatch");
        }
        private void NumberingUi()
        {
            var viewports=new List<Viewport>();
            using(var tx=new Transaction(doc,"Numbering fixture")) {tx.Start();for(int i=0;i<views.Count;i++)viewports.Add(Viewport.Create(doc,sheet.Id,views[i].Id,new XYZ(0.2+i*0.4,0.5,0)));tx.Commit();}
            Assert(RunCommand("ViewNumberingPlugin","ViewNumberingPlugin.Command",viewports.Select(v=>v.Id).ToList())==Result.Succeeded,"Numbering command failed");
            for(int i=0;i<viewports.Count;i++)Assert(viewports[i].get_Parameter(BuiltInParameter.VIEWPORT_DETAIL_NUMBER).AsString()==(i+1).ToString(),"Viewport detail number mismatch");
        }
        private void SheetsUi()
        {
            int types=new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsElementType().ToElementIds().Count;
            int before=new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).ToElementIds().Count;
            var result=RunCommand("CreateBlankSheetsPlugin","CreateBlankSheetsPlugin.CreateBlankSheetsCommand",new List<ElementId>());
            if(types==0){Assert(result==Result.Cancelled,"Missing titleblock guard");Write("LIMITATION: successful sheet creation not tested (no titleblock types)");return;}
            Assert(result==Result.Succeeded,"Sheet command failed");
            Assert(new FilteredElementCollector(doc).OfClass(typeof(ViewSheet)).ToElementIds().Count>before,"No new sheets");
        }
        private void WorksetsUi()
        {
            var result=RunCommand("CreateViewsForWorksets","WorksetViewsPlugin.WorksetViewsCommand",new List<ElementId>());
            Assert(result==(doc.IsWorkshared?Result.Succeeded:Result.Cancelled),"Workset guard result");
            if(!doc.IsWorkshared)Write("LIMITATION: workshared success path not tested; existing project has no worksharing");
        }
    }
}

namespace ARV.Tests
{
    // Никаких транзакций или изменений модели. Проверяет доступность в read-only API-контексте.
    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.ReadOnly)]
    public class ReadOnlyAvailabilityCheck : Autodesk.Revit.UI.IExternalCommand
    {
        public Autodesk.Revit.UI.Result Execute(Autodesk.Revit.UI.ExternalCommandData data, ref string message, Autodesk.Revit.DB.ElementSet elements)
        {
            string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), @"..\.."));
            var doc = data.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument) return Autodesk.Revit.UI.Result.Cancelled;
            var lines = new System.Collections.Generic.List<string>();
            lines.Add("ReadOnlyContext=" + doc.IsReadOnly);
            try
            {
                foreach (string name in new[] {"WallStartSchedule", "FinishingSchedulePlugin", "HideCategory", "HideSubCategory", "CreateBlankSheetsPlugin", "CopyViewsWithRename", "PlaceViewsOnSheet", "TightCropPlugin", "ViewNumberingPlugin", "CreateViewsForWorksets"})
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(System.IO.Path.Combine(root,"dist","Revit2022","ARV",name+".dll"));
                    var check = (Autodesk.Revit.UI.IExternalCommandAvailability)System.Activator.CreateInstance(assembly.GetType("ARV.Common.ProjectCommandAvailability", true));
                    bool enabled = check.IsCommandAvailable(data.Application, new Autodesk.Revit.DB.CategorySet());
                    lines.Add(name + " " + assembly.GetName().Version + " Enabled=" + enabled);
                    if (!enabled) throw new System.InvalidOperationException("Disabled: " + name);
                }
                System.IO.File.WriteAllLines(System.IO.Path.Combine(root,"artifacts","availability-native-report.txt"), lines);
                Autodesk.Revit.UI.TaskDialog.Show("ARV 1.1.1", "Все десять проверок доступности вернули true. Модель не изменялась.");
                return Autodesk.Revit.UI.Result.Succeeded;
            }
            catch (System.Exception ex)
            {
                message = ex.ToString(); lines.Add(message);
                System.IO.File.WriteAllLines(System.IO.Path.Combine(root,"artifacts","availability-native-report.txt"), lines);
                return Autodesk.Revit.UI.Result.Failed;
            }
        }
    }
}

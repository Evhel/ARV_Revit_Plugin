using Autodesk.Navisworks.Api.Interop.ComApi;
using Autodesk.Navisworks.Api.Plugins;
using Autodesk.Navisworks.Api;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ComApi = Autodesk.Navisworks.Api.Interop.ComApi;
using ComApiBridge = Autodesk.Navisworks.Api.ComApi;

[Plugin("ExportClashReports", "YourCompany", DisplayName = "Export Clash Reports")]
public class ExportClashReports : AddInPlugin
{
    private string logFilePath;

    public override int Execute(params string[] parameters)
    {
        try
        {
            Log("Плагин запущен");
            // Путь к Excel с конфигурацией (укажи правильный путь)
            string excelFilePath = @"C:\Path\To\Your\settings.xlsx";
            var jobs = ReadJobsFromExcel(excelFilePath);
            var comApiDoc = ComApiBridge.ToInwOpState(Application.ActiveDocument);
            foreach (var job in jobs)
            {
                string nwfFullPath = Path.Combine(job.FolderPathNwf, job.NwfFileName);
                Log($"Обработка файла: {nwfFullPath}");

                if (!File.Exists(nwfFullPath))
                {
                    Log($".nwf файл не найден: {nwfFullPath}");
                    continue;
                }

                Application.ActiveDocument.Clear();
                Application.ActiveDocument.OpenFile(nwfFullPath);

                var clashTests = comApiDoc.GetClash().Tests();

                string dateFolderHtml = CreateDateFolder(job.HtmlReportPath);
                string dateFolderXml = CreateDateFolder(job.XmlReportPath);
                string dateFolderNwd = CreateDateFolder(job.NwdSavePath);

                foreach (InwOaClashTest test in clashTests)
                {
                    int collisionsCount = test.GetCollisionCount();
                    Log($"Проверка \"{test.Name}\": коллизий {collisionsCount}");

                    if (collisionsCount == 0)
                    {
                        Log("Пропуск пустой проверки");
                        continue;
                    }
                    string safeName = MakeSafeFileName(test.Name);
                    string htmlPath = Path.Combine(dateFolderHtml, safeName + ".html");
                    string xmlPath = Path.Combine(dateFolderXml, safeName + ".xml");

                    test.ExportReport(htmlPath, nwClashReportFormat.nwReportHtml);
                    Log($"HTML отчет сохранен: {htmlPath}");

                    test.ExportReport(xmlPath, nwClashReportFormat.nwReportXml);
                    Log($"XML отчет сохранен: {xmlPath}");
                }

                string nwdFile = Path.Combine(dateFolderNwd, Path.GetFileNameWithoutExtension(nwfFullPath) + ".nwd");
                Application.ActiveDocument.SaveFile(nwdFile, DocumentFileVersion.Current);
                Log($".nwd сохранен: {nwdFile}");
            }

            Log("Плагин успешно завершил работу");
            return 0;
        }
        catch (Exception ex)
        {
            Log("Ошибка: " + ex.ToString());
            return -1;
        }
    }

    private List<NwfJob> ReadJobsFromExcel(string excelFilePath)
    {
        // Вставь сюда реализацию чтения из Excel (через ClosedXML или другой способ)
        return new List<NwfJob>();
    }

    private string CreateDateFolder(string baseFolder)
    {
        string dateFolder = Path.Combine(baseFolder, DateTime.Now.ToString("dd.MM.yyyy"));
        if (!Directory.Exists(dateFolder))
            Directory.CreateDirectory(dateFolder);
        return dateFolder;
    }

    private void Log(string message)
    {
        if (string.IsNullOrEmpty(logFilePath))
        {
            logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NavisReportLog.txt");
        }
        using (var sw = File.AppendText(logFilePath))
        {
            sw.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}");
        }
    }

    private string MakeSafeFileName(string fileName)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(c, '_');
        return fileName;
    }

    public class NwfJob
    {
        public string FolderPathNwf { get; set; }
        public string NwfFileName { get; set; }
        public string HtmlReportPath { get; set; }
        public string XmlReportPath { get; set; }
        public string NwdSavePath { get; set; }
    }
}

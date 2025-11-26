using Sdl.Core.PluginFramework;
using Sdl.Desktop.IntegrationApi;
using Sdl.Desktop.IntegrationApi.DefaultLocations;
using Sdl.Desktop.IntegrationApi.Extensions;
using Sdl.FileTypeSupport.Framework.BilingualApi;
using Sdl.TranslationStudioAutomation.IntegrationApi;
using Sdl.TranslationStudioAutomation.IntegrationApi.Extensions;
using Sdl.TranslationStudioAutomation.IntegrationApi.Presentation.DefaultLocations;
using Sdl.Desktop.IntegrationApi.Interfaces;
using System;
using System.IO;
using System.Windows.Forms;
using System.Net.Http;               // Added
using System.Text;                   // Added
using System.Threading.Tasks;        // Added
using Newtonsoft.Json;  // Add this at top with other using directives

namespace Eurolex
{
    [Action("SendSegmentAction",
        Name = "Send Segment",
        Description = "Displays the current segment texts.")]
    [ActionLayout(typeof(TranslationStudioDefaultContextMenus.EditorDocumentContextMenuLocation))]
    [RibbonGroup("Review", Name = "Segment Send", Description = "Segment Send", ContextByType = typeof(EditorController))]
    [RibbonGroupLayout(LocationByType = typeof(TranslationStudioDefaultRibbonTabs.AddinsRibbonTabLocation))]
    public class SendSegmentAction : AbstractAction
    {
        private static readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:6175") };
// Ensure the following using directive is present at the top of your file:


// To fix CS1069, you must add a reference to the System.Net.Http assembly in your project.
// In Visual Studio, right-click your project in Solution Explorer, choose "Add Reference...",
// then check "System.Net.Http" under Assemblies > Framework and click OK.

// No code changes are needed in the file itself beyond the using directive above.
        private System.Windows.Forms.Timer _segmentMonitorTimer;
        private string _lastSegmentId;

        protected override void Execute()
        {
            var editorController = SdlTradosStudio.Application.GetController<EditorController>();
            if (editorController?.ActiveDocument == null)
            {
                MessageBox.Show("No active document.");
                return;
            }

            var segment = editorController.ActiveDocument.ActiveSegmentPair;
            if (segment == null)
            {
                MessageBox.Show("No active segment.");
                return;
            }

            string source = segment.Source.ToString();
            string target = segment.Target.ToString();
            string prefixedSource = "@@@ERLX@@@" + source;
            var currentSegmentId = segment.Properties?.Id.Id;

            Clipboard.SetText(prefixedSource);

            MessageBox.Show($"Source 1:\n{source}\n\nTarget 1:\n{target}\n\nToClipboard 1:\n{prefixedSource}\n\nID 1:\n{currentSegmentId}", "Segment Content");
        }

        public void StartSegmentMonitoring()
        {
            MessageBox.Show("LegisTracerEU: Segment monitoring is initialized, Source Segment will be passed to LegisTracerEU to automatically look up references in EU Law.");
            _segmentMonitorTimer = new Timer();
            _segmentMonitorTimer.Interval = 1000;
            _segmentMonitorTimer.Tick += SegmentMonitorTimer_Tick;
            _segmentMonitorTimer.Start();
        }

        public override void Initialize()
        {
            Enabled = true;
            StartSegmentMonitoring();
        }

        private async void SegmentMonitorTimer_Tick(object sender, EventArgs e)
        {
            var editorController = SdlTradosStudio.Application.GetController<EditorController>();
            var activeDoc = editorController?.ActiveDocument;
            if (activeDoc == null)
                return;

            var segment = activeDoc.ActiveSegmentPair;
            if (segment == null || segment.Properties == null)
                return;

            var currentSegmentId = segment.Properties?.Id.Id;
            if (string.IsNullOrEmpty(currentSegmentId) || currentSegmentId == _lastSegmentId)
                return;

            _lastSegmentId = currentSegmentId;

            string source = segment.Source?.ToString() ?? "";
            string target = segment.Target?.ToString() ?? "";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string filePath = @"C:\Temp\segment_output.txt";

            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

          //  System.IO.File.WriteAllText(filePath, source);

            await SendSegmentToIngestAsync(source, target, currentSegmentId, timestamp).ConfigureAwait(false);
        }

        private static async Task SendSegmentToIngestAsync(string source, string target, string segmentId, string timestamp)
        {
            try
            {
                string json = BuildJson(source, target, segmentId, timestamp);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await _httpClient.PostAsync("/ingest", content).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine("Ingest POST failed: " + (int)response.StatusCode + " " + response.ReasonPhrase);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ingest POST exception: " + ex.Message);
            }
        }

        private static string BuildJson(string source, string target, string segmentId, string timestamp)
        {
            var payload = new
            {
                source,
                target,
                segmentId,
                timestamp
            };
            return JsonConvert.SerializeObject(payload);
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\r", "\\r")
                    .Replace("\n", "\\n");
        }
    }

    [ViewPart(
        Id = "LegisTracerEUResultsViewPart",
        Name = "LegisTracerEU Results",
        Description = "Displays processed EU Law search results")]
    [ViewPartLayout(typeof(EditorController), Dock = DockType.Bottom)]
    public class ResultsViewPart : AbstractViewPartController
    {
        private readonly ResultsControl _control = new ResultsControl();

        protected override void Initialize()
        {
            System.Diagnostics.Debug.WriteLine("ResultsViewPart.Initialize() called");
            _control.ShowInitialContent();
        }

        protected override IUIControl GetContentControl()
        {
            return _control;
        }

        public void SetHtml(string html)
        {
            _control.SetHtml(html);
        }
    }

    // New custom control class
    public class ResultsControl : UserControl, IUIControl
    {
        private WebBrowser _browser;
        private bool _isReady = false;
        private string _pendingHtml = null;

        public ResultsControl()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            _browser = new WebBrowser
            {
                Dock = DockStyle.Fill,
                AllowWebBrowserDrop = false,
                ScriptErrorsSuppressed = true
            };

            Controls.Add(_browser);
            
            // Wait for browser to be ready
            _browser.DocumentCompleted += Browser_DocumentCompleted;
            _browser.Navigate("about:blank");
        }

        private void Browser_DocumentCompleted(object sender, WebBrowserDocumentCompletedEventArgs e)
        {
            if (_browser.ReadyState == WebBrowserReadyState.Complete && e.Url.ToString() == "about:blank")
            {
                _browser.DocumentCompleted -= Browser_DocumentCompleted;
                _isReady = true;
                
                // If there's pending HTML, set it now
                if (_pendingHtml != null)
                {
                    SetHtmlInternal(_pendingHtml);
                    _pendingHtml = null;
                }
            }
        }

        public void SetHtml(string html)
        {
            if (_browser == null || _browser.IsDisposed)
                return;

            try
            {
                if (_browser.InvokeRequired)
                {
                    _browser.BeginInvoke(new Action(() => SetHtml(html)));
                }
                else
                {
                    if (_isReady)
                    {
                        SetHtmlInternal(html);
                    }
                    else
                    {
                        // Browser not ready yet, save for later
                        _pendingHtml = html;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SetHtml error: {ex.Message}");
            }
        }

        private void SetHtmlInternal(string html)
        {
            if (_browser != null && !_browser.IsDisposed)
            {
                try
                {
                    _browser.DocumentText = html;
                    System.Diagnostics.Debug.WriteLine($"HTML set successfully. Length: {html?.Length ?? 0}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SetHtmlInternal error: {ex.Message}");
                }
            }
        }

        public void ShowInitialContent()
        {
            var sb = new StringBuilder();
            sb.Append("<html><head><meta charset='utf-8'/>");
            sb.Append("<style>body{font-family:Segoe UI;font-size:12px;color:#222;margin:8px;} h1{font-size:16px;color:#164;} .box{margin-top:8px;padding:8px;border:1px solid #ddd;border-radius:4px;background:#f9f9f9;}</style>");
            sb.Append("</head><body>");
            sb.Append("<h1>LegisTracerEU Panel</h1>");
            sb.Append("<div class='box'>This is dummy content rendered as HTML inside the panel.</div>");
            sb.Append("<div class='box'>You can replace this with real results later.</div>");
            sb.Append("</body></html>");
            
            System.Diagnostics.Debug.WriteLine("ShowInitialContent called");
            SetHtml(sb.ToString());
        }
    }
}


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
        Name = "LegisTracerEU Search",
        Description = "Search the selected text or segment in EU Law references.")]
    [ActionLayout(typeof(TranslationStudioDefaultContextMenus.EditorDocumentContextMenuLocation))]
    [RibbonGroup("Review", Name = "LegisTracerEU Search", Description = "Search the selected text or segment in EU Law references", ContextByType = typeof(EditorController))]
    [RibbonGroupLayout(LocationByType = typeof(TranslationStudioDefaultRibbonTabs.AddinsRibbonTabLocation))]
    public class SendSegmentAction : AbstractAction
    {
        private static readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:6175") };
        internal static bool _iateSearchEnabled = false; // Track IATE checkbox state
// Ensure the following using directive is present at the top of your file:


// To fix CS1069, you must add a reference to the System.Net.Http assembly in your project.
// In Visual Studio, right-click your project in Solution Explorer, choose "Add Reference...",
// then check "System.Net.Http" under Assemblies > Framework and click OK.

// No code changes are needed in the file itself beyond the using directive above.
        private System.Windows.Forms.Timer _segmentMonitorTimer;
        private string _lastSegmentId;

        protected override async void Execute()
        {
            var editorController = SdlTradosStudio.Application.GetController<EditorController>();
            if (editorController?.ActiveDocument == null)
            {
                return;
            }

            var segment = editorController.ActiveDocument.ActiveSegmentPair;
            if (segment == null)
            {
                return;
            }

            // Get selected text in the active segment
            string selectedText = null;
            bool isSource = true;
            if (TryGetSelectedText(editorController.ActiveDocument, segment.Source?.ToString() ?? "", segment.Target?.ToString() ?? "", out selectedText, out isSource))
            {
                if (!string.IsNullOrWhiteSpace(selectedText))
                {
                    // Send the selection to the service
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string segmentId = segment.Properties?.Id.Id ?? "";
                    string target = segment.Target?.ToString() ?? "";

                    await SendSegmentToIngestAsync(selectedText, target, segmentId, timestamp).ConfigureAwait(false);
                    return;
                }
            }

            // Fallback: no selection, send full segment
            string source = segment.Source.ToString();
            string target2 = segment.Target.ToString();
            string timestamp2 = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string segmentId2 = segment.Properties?.Id.Id ?? "";
            
            await SendSegmentToIngestAsync(source, target2, segmentId2, timestamp2).ConfigureAwait(false);
        }

        public static string HtmlEncode(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        // Simple helper: encode the whole string for safety, then un-encode the
        // encoded form of `substring` and wrap it in a highlight span.
        public static string HighlightSubstring(string input, string substring, string highlightStyle = "background:yellow;font-weight:bold;")
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            if (string.IsNullOrEmpty(substring)) return HtmlEncode(input);

            // Encode entire input and substring
            var encodedInput = HtmlEncode(input);
            var encodedSub = HtmlEncode(substring);

            // Replace encoded occurrences of the substring with an unencoded span that
            // contains the encoded text (keeps content safe but allows the span to render)
            return encodedInput.Replace(encodedSub, $"<span style=\"{highlightStyle}\">{encodedSub}</span>");
        }


        public void StartSegmentMonitoring()
        {
           // MessageBox.Show("LegisTracerEU: Segment monitoring is initialized, Source Segment will be passed to LegisTracerEU to automatically look up references in EU Law.");
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
         //  string filePath = @"C:\Temp\segment_output.txt";

          //  string directory = Path.GetDirectoryName(filePath);
           // if (!Directory.Exists(directory))
           // {
           //     Directory.CreateDirectory(directory);
           // }

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

                    string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    UpdateViewPart(segmentId, source, responseContent);

                }   
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ingest POST exception: " + ex.Message);
            }
        }

        private static string BuildJson(string source, string target, string segmentId, string timestamp)
        {
            return BuildJsonPublic(source, target, segmentId, timestamp);
        }

        public static string BuildJsonPublic(string source, string target, string segmentId, string timestamp)
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

        private static void UpdateViewPart(string segmentId, string source, string responseJson)
        {
            UpdateViewPartPublic(segmentId, source, responseJson);
        }

        public static void UpdateViewPartPublic(string segmentId, string source, string responseJson)
        {
            try
            {
                var viewPart = SdlTradosStudio.Application.GetController<ResultsViewPart>();
                if (viewPart == null) return;

                SearchResponse resp = null;
                try
                {
                    resp = JsonConvert.DeserializeObject<SearchResponse>(responseJson);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine("JSON parse error: " + ex.Message);
                }

                var sb = new StringBuilder();
                sb.Append("<html><head><meta charset='utf-8'/>");
                sb.Append("<style>");
                sb.Append("body{font-family:Segoe UI;font-size:24px;color:#222;margin:8px;}");
                sb.Append("h1{font-size:32px;color:#164;margin:0 0 10px;}");
                sb.Append(".header{display:flex;justify-content:space-between;align-items:center;padding:12px;background:#036;color:#fff;margin-bottom:16px;border-radius:4px;}");
                sb.Append(".header h1{font-size:28px;color:#fff;margin:0;}");
                sb.Append(".about-btn{padding:8px 16px;background:#fff;color:#036;border:none;border-radius:3px;cursor:pointer;font-size:18px;font-weight:bold;}");
                sb.Append(".about-btn:hover{background:#f0f0f0;}");
                sb.Append(".modal{display:none;position:fixed;z-index:1000;left:0;top:0;width:100%;height:100%;background:rgba(0,0,0,0.5);}");
                sb.Append(".modal-content{background:#fff;margin:10% auto;padding:24px;border-radius:8px;width:80%;max-width:600px;box-shadow:0 4px 6px rgba(0,0,0,0.3);}");
                sb.Append(".modal-header{font-size:26px;font-weight:bold;color:#036;margin-bottom:16px;}");
                sb.Append(".modal-body{font-size:20px;line-height:1.6;}");
                sb.Append(".modal-body ol{margin:12px 0;padding-left:24px;}");
                sb.Append(".modal-body li{margin:8px 0;}");
                sb.Append(".modal-body a{color:#036;text-decoration:none;font-weight:bold;}");
                sb.Append(".modal-body a:hover{text-decoration:underline;}");
                sb.Append(".close-btn{float:right;font-size:32px;font-weight:bold;color:#999;cursor:pointer;line-height:20px;}");
                sb.Append(".close-btn:hover{color:#000;}");
                sb.Append(".item{margin:8px 0;padding:8px;border:1px solid #ddd;border-radius:4px;background:#f9f9f9;}");
                sb.Append(".celex{font-weight:bold;color:#036;margin-bottom:4px;} .snippet{white-space:pre-wrap;margin-top:6px;}");
                sb.Append(".search-bar{display:flex;align-items:center;gap:8px;margin-bottom:12px;padding:8px;background:#fff;border:1px solid #ccc;border-radius:4px;}");
                sb.Append(".search-input{flex:1;padding:6px;border:1px solid #999;border-radius:3px;font-size:24px;}");
                sb.Append(".search-btn{padding:6px 12px;background:#036;color:#fff;border:none;border-radius:3px;cursor:pointer;font-size:24px;}");
                sb.Append(".search-btn:hover{background:#025;}");
                sb.Append(".checkbox-label{display:flex;align-items:center;gap:4px;margin-left:auto;white-space:nowrap;}");
                sb.Append(".main-container{display:block;}");
                sb.Append(".results-section{width:100%;}");
                sb.Append(".terminology-section{display:none;width:100%;height:180px;overflow-y:auto;margin:16px 0;padding:12px;background:#f5f5f5;border:1px solid #ccc;border-radius:4px;}");
                sb.Append(".show-terminology .terminology-section{display:block;}");
                sb.Append(".terminology-section h3{margin:0 0 8px;font-size:28px;color:#036;}");
                sb.Append(".term-grid{display:grid;grid-template-columns:1fr 1fr;gap:8px;}");
                sb.Append(".term-row{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-bottom:8px;}");
                sb.Append(".term-item{padding:6px;background:#fff;border:1px solid #ddd;border-radius:3px;font-size:22px;}");
                sb.Append(".term-item strong{display:block;font-size:18px;color:#036;margin-bottom:4px;}");
                sb.Append(".segment-info{font-size:20px;color:#666;margin-bottom:8px;padding:4px;background:#fff;border:1px solid #ddd;border-radius:3px;}");
                sb.Append("</style>");
                sb.Append("<script>");
                sb.Append("function toggleTerminology(cb){");
                sb.Append("  document.body.classList.toggle('show-terminology',cb.checked);");
                sb.Append("  window.external.SetIateEnabled(cb.checked);");
                sb.Append("}");
                sb.Append("function doSearch(){");
                sb.Append("  var q=document.getElementById('searchInput').value;");
                sb.Append("  if(q){");
                sb.Append("    window.external.SearchManual(q);");
                sb.Append("  }");
                sb.Append("}");
                sb.Append("function showAbout(e){");
                sb.Append("  if(e){e.preventDefault();e.stopPropagation();}");
                sb.Append("  document.getElementById('aboutModal').style.display='block';");
                sb.Append("  return false;");
                sb.Append("}");
                sb.Append("function closeAbout(e){");
                sb.Append("  if(e){e.preventDefault();e.stopPropagation();}");
                sb.Append("  document.getElementById('aboutModal').style.display='none';");
                sb.Append("  return false;");
                sb.Append("}");
                sb.Append("document.addEventListener('click',function(e){");
                sb.Append("  var modal=document.getElementById('aboutModal');");
                sb.Append("  if(e.target==modal){closeAbout(e);}");
                sb.Append("});");
                sb.Append("</script>");
                sb.Append("</head><body>");

                // Header with title and About button
                sb.Append("<div class='header'>");
                sb.Append("<h1>LegisTracerEU - Full-text search in Eur-Lex and IATE</h1>");
                sb.Append("<button class='about-btn' onclick='return showAbout(event);'>About</button>");
                sb.Append("</div>");

                // About Modal
                sb.Append("<div id='aboutModal' class='modal' onclick='if(event.target==this)closeAbout(event);'>");
                sb.Append("<div class='modal-content' onclick='event.stopPropagation();'>");
                sb.Append("<span class='close-btn' onclick='return closeAbout(event);'>&times;</span>");
                sb.Append("<div class='modal-header'>About LegisTracerEU</div>");
                sb.Append("<div class='modal-body'>");
                sb.Append("<p>To use this plugin to search EU law and terminology, you must have the LegisTracerEU app installed and running.</p>");
                sb.Append("<p><strong>Follow these steps:</strong></p>");
                sb.Append("<ol>");
                sb.Append("<li>Install the app from Microsoft Store: <a href='https://apps.microsoft.com/detail/9NKNVGXJFSW5' target='_blank'>Download here</a></li>");
                sb.Append("<li>Subscribe at <a href='https://www.pts-translation.sk' target='_blank'>www.pts-translation.sk</a></li>");
                sb.Append("<li>Enter your email and Passkey in the app</li>");
                sb.Append("</ol>");
                sb.Append("<p>For more information, visit <a href='https://www.pts-translation.sk' target='_blank'>www.pts-translation.sk</a></p>");
                sb.Append("</div>");
                sb.Append("</div>");
                sb.Append("</div>");

                // Search bar with manual search and IATE checkbox
                sb.Append("<div class='search-bar'>");
                sb.Append("<input type='text' id='searchInput' class='search-input' placeholder='Enter search term...' />");
                sb.Append("<button class='search-btn' onclick='doSearch()'>Search</button>");
                sb.Append("<label class='checkbox-label'>");
                sb.Append("<input type='checkbox' id='iateCheck' onchange='toggleTerminology(this)'");
                if (_iateSearchEnabled)
                {
                    sb.Append(" checked");
                }
                sb.Append(" /> IATE Search");
                sb.Append("</label>");
                sb.Append("</div>");
                
                // Add script to set initial body class based on checkbox state
                sb.Append("<script>");
                if (_iateSearchEnabled)
                {
                    sb.Append("document.body.classList.add('show-terminology');");
                }
                sb.Append("</script>");

                // Main container
                sb.Append("<div class='main-container'>");
                
                if (resp == null)
                {
                    sb.Append("<div class='segment-info'><span style='font-weight:bold;'>Segment:</span> ").Append(HtmlEncode(segmentId)).Append("</div>");
                    
                    // Terminology section (hidden by default, below segment info)
                    sb.Append("<div class='terminology-section'>");
                    sb.Append("<h3>IATE Terminology</h3>");
                    sb.Append("<div class='term-item' style='color:#666;font-style:italic;'>No terminology data available</div>");
                    sb.Append("</div>");
                    
                    sb.Append("<div class='item'>Response parse error or empty response.</div>");
                }
                else if (resp.results == null || resp.results.Length == 0)
                {
                    sb.Append("<div class='segment-info'><span style='font-weight:bold;'>Segment:</span> ").Append(HtmlEncode(segmentId)).Append("</div>");
                    
                    // Terminology section (hidden by default, below segment info)
                    sb.Append("<div class='terminology-section'>");
                    sb.Append("<h3>IATE Terminology</h3>");
                    sb.Append("<div class='term-item' style='color:#666;font-style:italic;'>No terminology data available</div>");
                    sb.Append("</div>");
                    
                    sb.Append("<div class='item'>No results.</div>");
                }
                else
                {
                    sb.Append("<div class='segment-info'><span style='font-weight:bold;'>Segment:</span> ").Append(HtmlEncode(segmentId));
                    sb.Append(" &nbsp;|&nbsp; <span style='font-weight:bold;'>Found:</span> ").Append(HtmlEncode(resp.count.ToString())).Append("</div>");
                    
                    // Terminology section (hidden by default, below segment info)
                    sb.Append("<div class='terminology-section'>");
                    sb.Append("<h3>IATE Terminology</h3>");
                    
                    if (resp.iate != null && resp.iate.Length > 0)
                    {
                        // Display actual IATE terminology results in two-column rows
                        foreach (var iateEntry in resp.iate)
                        {
                            sb.Append("<div class='term-row'>");
                            sb.Append("<div class='term-item'>").Append(HtmlEncode(iateEntry.iatesource ?? "")).Append("</div>");
                            sb.Append("<div class='term-item'>").Append(HtmlEncode(iateEntry.iatetarget ?? "")).Append("</div>");
                            sb.Append("</div>");
                        }
                    }
                    else
                    {
                        sb.Append("<div class='term-item' style='color:#666;font-style:italic;'>No terminology results found</div>");
                    }
                    
                    sb.Append("</div>");
                    
                    foreach (var r in resp.results)
                    {
                        sb.Append("<div class='item'>");
                        sb.Append("<div class='celex'>CELEX: ").Append(HtmlEncode(r.celex ?? "")).Append("</div>");

                        var lang1Code = string.IsNullOrEmpty(r.lang1) ? "?" : HtmlEncode(r.lang1);
                        if (!string.IsNullOrEmpty(r.lang1_result))
                        {
                            sb.Append($"<div><strong>{lang1Code}:</strong></div>");
                            sb.Append("<div class='snippet'>")
                              .Append(HighlightSubstring(r.lang1_result, source))
                              .Append("</div>");
                        }

                        var lang2Code = string.IsNullOrEmpty(r.lang2) ? "?" : HtmlEncode(r.lang2);
                        if (!string.IsNullOrEmpty(r.lang2_result))
                        {
                            sb.Append($"<div style='margin-top:6px;'><strong>{lang2Code}:</strong></div>");
                            sb.Append("<div class='snippet'>")
                              .Append(HighlightSubstring(r.lang2_result, source))
                              .Append("</div>");
                        }
                        sb.Append("</div>");
                    }
                }
                
                sb.Append("</div>"); // end main-container

                sb.Append("</body></html>");

                viewPart.SetHtml(sb.ToString());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"UpdateViewPart error: {ex.Message}");
            }
        }


        // Get selected text using the standard Trados API approach
        private static bool TryGetSelectedText(object activeDoc, string source, string target, out string selectedText, out bool isSource)
        {
            selectedText = null;
            isSource = true;

            try
            {
                // Cast to the correct type to access Selection.Current
                var document = activeDoc as Sdl.TranslationStudioAutomation.IntegrationApi.IStudioDocument;
                if (document == null)
                    return false;

                // Use Selection.Current.ToString() as shown in the IATE plugin
                selectedText = document.Selection?.Current.ToString().TrimEnd() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(selectedText))
                    return false;

                selectedText = selectedText.Trim();

                // Determine if selection is from source or target using FocusedDocumentContent
                if (document.FocusedDocumentContent == FocusedDocumentContent.Target)
                {
                    isSource = false;
                }
                else
                {
                    isSource = true;
                }

                return true;
            }
            catch
            {
                selectedText = null;
                isSource = true;
                return false;
            }
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
                ScriptErrorsSuppressed = true,
                ObjectForScripting = new ScriptCallbackHandler()
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
            sb.Append("<style>body{font-family:Segoe UI;font-size:24px;color:#222;margin:8px;} h1{font-size:32px;color:#164;} .box{margin-top:8px;padding:8px;border:1px solid #ddd;border-radius:4px;background:#f9f9f9;}</style>");
            sb.Append("</head><body>");
            sb.Append("<h1>LegisTracerEU Results Panel</h1>");
            sb.Append("<div class='box'>When a segment is opened, it is automatically searched against EU Law.</div>");
            sb.Append("<div class='box'>Search results appear in this pane.</div>");
            sb.Append("</body></html>");
            
            System.Diagnostics.Debug.WriteLine("ShowInitialContent called");
            SetHtml(sb.ToString());
        }
    }

    // COM-visible class to handle JavaScript callbacks
    [System.Runtime.InteropServices.ComVisibleAttribute(true)]
    public class ScriptCallbackHandler
    {
        private static readonly HttpClient _httpClient = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:6175") };

        public void SetIateEnabled(bool enabled)
        {
            SendSegmentAction._iateSearchEnabled = enabled;
            System.Diagnostics.Debug.WriteLine($"IATE search enabled set to: {enabled}");
        }

        public async void SearchManual(string searchText)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return;

            try
            {
                System.Diagnostics.Debug.WriteLine($"Manual search triggered for: {searchText}");
                
                // Get the editor controller to retrieve segment ID
                var editorController = SdlTradosStudio.Application.GetController<EditorController>();
                var activeDoc = editorController?.ActiveDocument;
                var segment = activeDoc?.ActiveSegmentPair;
                
                string segmentId = segment?.Properties?.Id.Id ?? "manual-search";
                string target = segment?.Target?.ToString() ?? "";
                string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // Build JSON payload using the same method
                string json = SendSegmentAction.BuildJsonPublic(searchText, target, segmentId, timestamp);
                
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                {
                    var response = await _httpClient.PostAsync("/ingest", content).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        System.Diagnostics.Debug.WriteLine("Manual search POST failed: " + (int)response.StatusCode + " " + response.ReasonPhrase);
                    }

                    string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    SendSegmentAction.UpdateViewPartPublic(segmentId, searchText, responseContent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Manual search exception: {ex.Message}");
            }
        }
    }

    
   
}

internal class SearchResponse
{
    public string status { get; set; }
    public int count { get; set; }
    public SearchResult[] results { get; set; }
    public IATEResult[] iate { get; set; }
}

internal class SearchResult
{
    public string lang1_result { get; set; }
    public string lang2_result { get; set; }
    public string celex { get; set; }

    public string lang1 { get; set; }
    public string lang2 { get; set; }
}

internal class IATEResult
{
    public string iatesource { get; set; }
    public string iatetarget { get; set; }
}

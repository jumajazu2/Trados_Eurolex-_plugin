using Sdl.Core.PluginFramework;
using Sdl.Desktop.IntegrationApi;
using Sdl.Desktop.IntegrationApi.DefaultLocations;
using Sdl.Desktop.IntegrationApi.Extensions;
using Sdl.FileTypeSupport.Framework.BilingualApi;
using Sdl.TranslationStudioAutomation.IntegrationApi;
using Sdl.TranslationStudioAutomation.IntegrationApi.Extensions;
using Sdl.TranslationStudioAutomation.IntegrationApi.Presentation.DefaultLocations;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;






namespace Eurolex
{
    [Action("SendSegmentAction",
        Name = "Send Segment",
        Description = "Displays the current segment texts.")]

    /*[ActionLayout(typeof(MyRibbonGroup), displayType: DisplayType.Large)]*/

    //[ActionLayout(typeof(TranslationStudioDefaultRibbonGroups), 20, DisplayType.Large)]

   
	[ActionLayout(typeof(TranslationStudioDefaultContextMenus.EditorDocumentContextMenuLocation))]
    // [ActionLayout(typeof(StudioDefaultRibbonGroups.TranslationMemoriesGroup), 1, DisplayType.Default)]

    [RibbonGroup("Review", Name = "Segment Send", Description = "Segment Send", ContextByType = typeof(EditorController))]
    [RibbonGroupLayout(LocationByType = typeof(TranslationStudioDefaultRibbonTabs.AddinsRibbonTabLocation))]



    public class SendSegmentAction : AbstractAction
    {
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

          //if currentSegmentId changes... monitoring for this,meaning going to another segment
          //possibly write to file and monitor file in flute
        }

      

        private System.Windows.Forms.Timer _segmentMonitorTimer;
        private string _lastSegmentId;
        public void StartSegmentMonitoring()
        {
            MessageBox.Show("Timer initialized!");
            _segmentMonitorTimer = new Timer();
            _segmentMonitorTimer.Interval = 1000; // every second
            _segmentMonitorTimer.Tick += SegmentMonitorTimer_Tick;
            _segmentMonitorTimer.Start();
        }



        public override void Initialize()
        {
            Enabled = true;
            

            StartSegmentMonitoring();  // Start the timer when plugin is initialized
        }

    
        

        private void SegmentMonitorTimer_Tick(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Timer tick");
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

            string prefixedSource = "@@@ERLX@@@" + source;
            // Clipboard.SetText(prefixedSource + "\n" + target);
            Clipboard.SetText(prefixedSource);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string filePath = @"C:\Temp\segment_output.txt"; // Or any valid path

            string directory = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string output = $"[{timestamp}]@@@{source}";


            System.IO.File.WriteAllText(filePath, source);


        }
    }
}


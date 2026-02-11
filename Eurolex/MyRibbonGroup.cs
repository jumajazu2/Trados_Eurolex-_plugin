using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sdl.Desktop.IntegrationApi;
using Sdl.Desktop.IntegrationApi.Extensions;
using Sdl.TranslationStudioAutomation.IntegrationApi;

namespace Eurolex
{
    [RibbonGroup("LegisTracerEURibbonGroup", Name = "LegisTracerEU")]
    [RibbonGroupLayout(LocationByType = typeof(Sdl.TranslationStudioAutomation.IntegrationApi.Presentation.DefaultLocations.TranslationStudioDefaultRibbonTabs.AddinsRibbonTabLocation))]
    public class LegisTracerEURibbonGroup : AbstractRibbonGroup
    {
    }

    [Action("LegisTracerEURibbonSearchAction",
        Name = "LegisTracerEU Search",
        Description = "Search EU Law and Terminology",
        Icon = "LegisTracerEU_Icon_64")]
    [ActionLayout(typeof(LegisTracerEURibbonGroup), 10, DisplayType.Large)]
    public class LegisTracerEURibbonAction : AbstractAction
    {
        protected override void Execute()
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

            string source = segment.Source?.ToString() ?? "";
            string target = segment.Target?.ToString() ?? "";
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string segmentId = segment.Properties?.Id.Id ?? "";

            System.Threading.Tasks.Task.Run(async () =>
            {
                try
                {
                    string json = SendSegmentAction.BuildJsonPublic(source, target, segmentId, timestamp, isManualSearch: false);
                    using (var httpClient = new System.Net.Http.HttpClient { BaseAddress = new Uri("http://127.0.0.1:6175") })
                    using (var content = new System.Net.Http.StringContent(json, Encoding.UTF8, "application/json"))
                    {
                        var response = await httpClient.PostAsync("/ingest", content).ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            string responseContent = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                            SendSegmentAction.UpdateViewPartPublic(segmentId, source, responseContent);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ribbon action exception: {ex.Message}");
                }
            });
        }
    }
}

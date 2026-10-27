using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Views;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Views
{
    /// <summary>
    /// set_link_visibility — masque ou affiche une occurrence de lien Revit dans une vue
    /// (la vue active par défaut). Refuse une vue pilotée par un gabarit, sauf force.
    /// </summary>
    public class SetLinkVisibilityCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetLinkVisibilityEventHandler _handler => (SetLinkVisibilityEventHandler)Handler;

        public override string CommandName => "set_link_visibility";

        public SetLinkVisibilityCommand(UIApplication uiApp)
            : base(new SetLinkVisibilityEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    long? linkId = parameters?["linkId"]?.Value<long>();
                    if (!linkId.HasValue)
                        throw new ArgumentException("linkId est obligatoire (voir list_links)");

                    bool? visible = parameters?["visible"]?.Value<bool>();
                    if (!visible.HasValue)
                        throw new ArgumentException("visible est obligatoire (true ou false)");

                    _handler.LinkId = linkId.Value;
                    _handler.Visible = visible.Value;
                    _handler.ViewId = parameters?["viewId"]?.Value<long>();
                    _handler.Force = parameters?["force"]?.Value<bool>() ?? false;

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("set_link_visibility : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"set_link_visibility a échoué : {ex.Message}");
                }
            }
        }
    }
}

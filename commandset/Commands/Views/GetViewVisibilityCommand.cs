using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Views;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Views
{
    /// <summary>
    /// get_view_visibility — état de visibilité d'une vue (la vue active par défaut) : gabarit,
    /// liens masqués ou non, catégories masquées. Lecture seule.
    /// </summary>
    public class GetViewVisibilityCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private GetViewVisibilityEventHandler _handler => (GetViewVisibilityEventHandler)Handler;

        public override string CommandName => "get_view_visibility";

        public GetViewVisibilityCommand(UIApplication uiApp)
            : base(new GetViewVisibilityEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    _handler.ViewId = parameters?["viewId"]?.Value<long>();

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("get_view_visibility : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"get_view_visibility a échoué : {ex.Message}");
                }
            }
        }
    }
}

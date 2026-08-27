using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// list_links — énumère les modèles liés du projet (chemin, statut,
    /// épinglage) et fournit le linkId attendu par les outils de lecture
    /// pour interroger le contenu d'un lien.
    /// </summary>
    public class ListLinksCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private ListLinksEventHandler _handler => (ListLinksEventHandler)Handler;

        public override string CommandName => "list_links";

        public ListLinksCommand(UIApplication uiApp)
            : base(new ListLinksEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("list_links : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"list_links a échoué : {ex.Message}");
                }
            }
        }
    }
}

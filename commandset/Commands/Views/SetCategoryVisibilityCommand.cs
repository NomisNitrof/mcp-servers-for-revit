using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Views;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Views
{
    /// <summary>
    /// set_category_visibility — masque ou affiche une catégorie (nom affiché) dans une vue
    /// (la vue active par défaut). Refuse une vue pilotée par un gabarit, sauf force.
    /// </summary>
    public class SetCategoryVisibilityCommand : ExternalEventCommandBase
    {
        private static readonly object _executionLock = new object();
        private SetCategoryVisibilityEventHandler _handler => (SetCategoryVisibilityEventHandler)Handler;

        public override string CommandName => "set_category_visibility";

        public SetCategoryVisibilityCommand(UIApplication uiApp)
            : base(new SetCategoryVisibilityEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    if (parameters?["linkId"] != null && parameters["linkId"].Type != JTokenType.Null)
                        throw new ArgumentException(
                            "linkId n'est pas supporté : l'API Revit ne permet pas de masquer une catégorie pour un " +
                            "seul lien (utiliser set_link_visibility pour masquer le lien entier)");

                    string category = parameters?["category"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(category))
                        throw new ArgumentException("category est obligatoire et non vide");

                    bool? visible = parameters?["visible"]?.Value<bool>();
                    if (!visible.HasValue)
                        throw new ArgumentException("visible est obligatoire (true ou false)");

                    _handler.Category = category;
                    _handler.ExactCategory = parameters?["exactCategory"]?.Value<bool>() ?? false;
                    _handler.Visible = visible.Value;
                    _handler.ViewId = parameters?["viewId"]?.Value<long>();
                    _handler.Force = parameters?["force"]?.Value<bool>() ?? false;

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("set_category_visibility : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"set_category_visibility a échoué : {ex.Message}");
                }
            }
        }
    }
}

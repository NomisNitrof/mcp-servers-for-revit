using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Masque ou affiche une occurrence de lien Revit (RevitLinkInstance)
    /// dans une vue : View.HideElements / UnhideElements sur l'instance.
    /// Une transaction nommée, annulée en cas d'exception.
    ///
    /// Vue pilotée par un gabarit qui contrôle « V/G Liens RVT » : refus net,
    /// sauf Force — la modification porte alors sur le gabarit.
    /// </summary>
    public class SetLinkVisibilityEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long LinkId { get; set; }
        public bool Visible { get; set; }
        public long? ViewId { get; set; }
        public bool Force { get; set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 15000)
        {
            _resetEvent.Reset();
            return _resetEvent.WaitOne(timeoutMilliseconds);
        }

        public void Execute(UIApplication app)
        {
            IsSuccess = false;
            ErrorMessage = null;
            Result = null;
            try
            {
                if (!ViewVisibilityUtils.TryResolveView(app, ViewId, out Document doc, out View vue, out string erreur))
                {
                    ErrorMessage = erreur;
                    return;
                }

                var instance = doc.GetElement(FamilyParameterUtils.IdFromValue(LinkId)) as RevitLinkInstance;
                if (instance == null)
                {
                    ErrorMessage = $"aucune occurrence de lien Revit (RevitLinkInstance) avec l'id {LinkId} — " +
                                   "utiliser list_links pour obtenir les ids valides";
                    return;
                }

                if (!ViewVisibilityUtils.TryResolveWriteTarget(
                        doc, vue, BuiltInParameter.VIS_GRAPHICS_RVT_LINKS, Force, out CibleVue cible, out erreur))
                {
                    ErrorMessage = erreur;
                    return;
                }
                View ecriture = cible.Ecriture;

                bool etaitVisible = !instance.IsHidden(ecriture);
                bool modifie = etaitVisible != Visible;

                if (modifie)
                {
                    if (!Visible && !instance.CanBeHidden(ecriture))
                    {
                        ErrorMessage = $"Revit refuse de masquer le lien {LinkId} dans la vue « {ecriture.Name} »";
                        return;
                    }

                    var ids = new List<ElementId> { instance.Id };
                    ViewVisibilityUtils.Ecrire(doc, "MCP — Visibilité d'un lien", () =>
                    {
                        if (Visible)
                            ecriture.UnhideElements(ids);
                        else
                            ecriture.HideElements(ids);
                    });
                }

                var linkType = doc.GetElement(instance.GetTypeId()) as RevitLinkType;
                Result = new
                {
                    link = new
                    {
                        linkId = instance.Id.GetValue(),
                        name = instance.Name,
                        fileName = linkType?.Name,
                        isLoaded = instance.GetLinkDocument() != null
                    },
                    view = ViewVisibilityUtils.DecrireVue(doc, vue),
                    visible = !instance.IsHidden(ecriture),
                    wasVisible = etaitVisible,
                    changed = modifie,
                    templateModified = cible.GabaritModifie,
                    modifiedView = new
                    {
                        viewId = ecriture.Id.GetValue(),
                        name = ecriture.Name,
                        isTemplate = ecriture.IsTemplate
                    }
                };
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "SetLinkVisibility";
        }
    }
}

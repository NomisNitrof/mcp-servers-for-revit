using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// Masque ou affiche une catégorie dans une vue : View.SetCategoryHidden
    /// sur la catégorie du document hôte. Une transaction nommée, annulée en
    /// cas d'exception.
    ///
    /// Portée : la vue hôte. Les modèles liés affichés « Selon la vue hôte »
    /// suivent ; l'API Revit ne permet pas de masquer une catégorie pour UN
    /// SEUL lien (RevitLinkGraphicsSettings n'expose pas les catégories) —
    /// pour cela, set_link_visibility masque le lien entier.
    ///
    /// Vue pilotée par un gabarit qui contrôle le volet de la catégorie
    /// (modèle, annotation, analytique) : refus net, sauf Force.
    /// </summary>
    public class SetCategoryVisibilityEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public string Category { get; set; }
        public bool ExactCategory { get; set; }
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

                if (!ViewVisibilityUtils.TryResolveCategory(doc, Category, ExactCategory, out Category categorie, out erreur))
                {
                    ErrorMessage = erreur;
                    return;
                }

                if (!ViewVisibilityUtils.TryResolveWriteTarget(
                        doc, vue, ViewVisibilityUtils.VoletDeCategorie(categorie), Force, out CibleVue cible, out erreur))
                {
                    ErrorMessage = erreur;
                    return;
                }
                View ecriture = cible.Ecriture;

                if (!ecriture.CanCategoryBeHidden(categorie.Id))
                {
                    ErrorMessage = $"la catégorie « {categorie.Name} » ne peut pas être masquée dans la vue « {ecriture.Name} » " +
                                   $"({ecriture.ViewType})";
                    return;
                }

                bool etaitMasquee = ecriture.GetCategoryHidden(categorie.Id);
                bool modifie = etaitMasquee == Visible;

                if (modifie)
                {
                    ViewVisibilityUtils.Ecrire(doc, "MCP — Visibilité d'une catégorie", () =>
                    {
                        ecriture.SetCategoryHidden(categorie.Id, !Visible);
                    });
                }

                Result = new
                {
                    category = new
                    {
                        categoryId = categorie.Id.GetValue(),
                        name = categorie.Name,
                        categoryType = categorie.CategoryType.ToString()
                    },
                    view = ViewVisibilityUtils.DecrireVue(doc, vue),
                    visible = !ecriture.GetCategoryHidden(categorie.Id),
                    wasVisible = !etaitMasquee,
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
            return "SetCategoryVisibility";
        }
    }
}

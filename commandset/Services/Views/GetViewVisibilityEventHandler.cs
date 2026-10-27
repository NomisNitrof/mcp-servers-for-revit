using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Views
{
    /// <summary>
    /// État de visibilité d'une vue : identité, gabarit appliqué (et ce qu'il
    /// verrouille), état de chaque lien (masqué ou non, mode d'affichage) et
    /// catégories masquées. Lecture seule, pas de transaction.
    /// </summary>
    public class GetViewVisibilityEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        // Plafond de la liste des catégories masquées : canal socket ~8 Ko.
        private const int MaxCategories = 120;

        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long? ViewId { get; set; }

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

                bool graphiques = vue.AreGraphicsOverridesAllowed();

                // Gabarit : ce qu'il verrouille parmi les volets de V/G.
                View gabarit = null;
                if (!vue.IsTemplate && vue.ViewTemplateId != ElementId.InvalidElementId)
                    gabarit = doc.GetElement(vue.ViewTemplateId) as View;
                var volets = new (string nom, BuiltInParameter id)[]
                {
                    ("modele", BuiltInParameter.VIS_GRAPHICS_MODEL),
                    ("annotation", BuiltInParameter.VIS_GRAPHICS_ANNOTATION),
                    ("analytique", BuiltInParameter.VIS_GRAPHICS_ANALYTICAL_MODEL),
                    ("liensRevit", BuiltInParameter.VIS_GRAPHICS_RVT_LINKS)
                };
                object gabaritInfo = null;
                if (gabarit != null)
                {
                    gabaritInfo = new
                    {
                        templateId = gabarit.Id.GetValue(),
                        name = gabarit.Name,
                        controlledVisibility = volets
                            .Where(v => ViewVisibilityUtils.GabaritControle(gabarit, v.id))
                            .Select(v => v.nom)
                            .ToList()
                    };
                }

                // Liens : une ligne par occurrence.
                var instances = new FilteredElementCollector(doc)
                    .OfClass(typeof(RevitLinkInstance))
                    .Cast<RevitLinkInstance>()
                    .OrderBy(i => i.Id.GetValue())
                    .ToList();
                var liens = instances.Select(instance =>
                {
                    var linkType = doc.GetElement(instance.GetTypeId()) as RevitLinkType;
                    bool? masque = null;
                    try { masque = instance.IsHidden(vue); } catch { }

                    string mode = null;
                    string source = null;
                    long? vueLiee = null;
                    if (graphiques)
                    {
                        // Réglages propres à l'occurrence, sinon à son type.
                        var reglages = LireReglages(vue, instance.Id);
                        source = reglages != null ? "instance" : null;
                        if (reglages == null)
                        {
                            reglages = LireReglages(vue, instance.GetTypeId());
                            source = reglages != null ? "type" : null;
                        }
                        if (reglages != null)
                        {
                            mode = reglages.LinkVisibilityType.ToString();
                            if (reglages.LinkedViewId != ElementId.InvalidElementId)
                                vueLiee = reglages.LinkedViewId.GetValue();
                        }
                    }

                    return new
                    {
                        linkId = instance.Id.GetValue(),
                        name = instance.Name,
                        fileName = linkType?.Name,
                        isLoaded = instance.GetLinkDocument() != null,
                        hidden = masque,
                        displayMode = mode,
                        displayModeSource = source,
                        linkedViewId = vueLiee
                    };
                }).ToList();

                // Catégories masquées individuellement (niveau supérieur).
                var masquees = new List<string>();
                if (graphiques)
                {
                    foreach (Category c in doc.Settings.Categories)
                    {
                        if (c == null || c.CategoryType == CategoryType.Internal || c.CategoryType == CategoryType.Invalid)
                            continue;
                        try
                        {
                            if (vue.CanCategoryBeHidden(c.Id) && vue.GetCategoryHidden(c.Id))
                                masquees.Add(c.Name);
                        }
                        catch
                        {
                            // Catégorie sans réglage dans ce type de vue : ignorée.
                        }
                    }
                    masquees.Sort(StringComparer.CurrentCultureIgnoreCase);
                }

                Result = new
                {
                    view = ViewVisibilityUtils.DecrireVue(doc, vue),
                    graphicsOverridesAllowed = graphiques,
                    template = gabaritInfo,
                    links = new
                    {
                        total = liens.Count,
                        hidden = liens.Count(l => l.hidden == true),
                        items = liens
                    },
                    hiddenCategories = new
                    {
                        total = masquees.Count,
                        truncated = masquees.Count > MaxCategories,
                        names = masquees.Take(MaxCategories).ToList()
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

        private static RevitLinkGraphicsSettings LireReglages(View vue, ElementId id)
        {
            try { return vue.GetLinkOverrides(id); }
            catch { return null; }
        }

        public string GetName()
        {
            return "GetViewVisibility";
        }
    }
}

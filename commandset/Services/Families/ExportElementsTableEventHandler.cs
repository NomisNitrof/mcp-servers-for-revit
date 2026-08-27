using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Produit une TABLE : une ligne par élément d'une catégorie, une colonne
    /// par paramètre demandé — dans le document hôte ou dans un modèle lié.
    /// C'est la brique du flux « listes et quantités » vers un chiffrier
    /// (décision D5) : le pendant lisible d'une nomenclature Revit.
    ///
    /// Un paramètre absent de l'occurrence est cherché sur le TYPE
    /// (TypeFallback), comme le fait une nomenclature ; les colonnes
    /// introuvables sont signalées dans missingParameters plutôt que rendues
    /// silencieusement vides.
    /// </summary>
    public class ExportElementsTableEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long? LinkId { get; set; }
        public string Category { get; set; }
        public List<string> ParameterNames { get; set; }
        public bool ExactCategory { get; set; }
        public bool TypeFallback { get; set; }
        public int Offset { get; set; }
        public int Limit { get; set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 30000)
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
                if (!LinkUtils.TryResolveDocument(app, LinkId, out Document doc, out string erreurDoc))
                {
                    ErrorMessage = erreurDoc;
                    return;
                }

                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .Where(e => CorrespondCategorie(e.Category.Name))
                    .OrderBy(e => e.Id.GetValue())
                    .ToList();

                var page = elements.Skip(Offset).Take(Limit).ToList();

                // Colonnes jamais résolues sur AUCUNE ligne de la page : très
                // probablement un nom mal orthographié ou d'une autre langue.
                var trouvees = new HashSet<string>();

                var lignes = page.Select(e =>
                {
                    var valeurs = new Dictionary<string, object>();
                    foreach (var nom in ParameterNames)
                    {
                        Parameter p = TrouverParametre(doc, e, nom);
                        if (p == null)
                        {
                            valeurs[nom] = null;
                            continue;
                        }
                        trouvees.Add(nom);
                        valeurs[nom] = new
                        {
                            value = ExtraireValeur(p),
                            display = FamilyParameterUtils.AffichageParametre(doc, p)
                        };
                    }
                    return new
                    {
                        elementId = e.Id.GetValue(),
                        name = SafeName(e),
                        typeName = SafeTypeName(doc, e),
                        values = valeurs
                    };
                }).ToList();

                var manquantes = ParameterNames.Where(n => !trouvees.Contains(n)).ToList();

                Result = new
                {
                    documentTitle = doc.Title,
                    linkId = LinkId,
                    category = Category,
                    total = elements.Count,
                    offset = Offset,
                    returned = lignes.Count,
                    hasMore = Offset + lignes.Count < elements.Count,
                    columns = ParameterNames,
                    missingParameters = manquantes,
                    rows = lignes
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

        /// <summary>Paramètre d'occurrence, sinon de type si TypeFallback.</summary>
        private Parameter TrouverParametre(Document doc, Element e, string nom)
        {
            try
            {
                var p = e.LookupParameter(nom);
                if (p != null)
                    return p;
                if (!TypeFallback)
                    return null;
                var typeId = e.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId)
                    return null;
                return doc.GetElement(typeId)?.LookupParameter(nom);
            }
            catch
            {
                return null;
            }
        }

        private static object ExtraireValeur(Parameter p)
        {
            if (!p.HasValue)
                return null;
            switch (p.StorageType)
            {
                case StorageType.String: return p.AsString();
                case StorageType.Integer: return p.AsInteger();
                case StorageType.Double: return p.AsDouble();
                case StorageType.ElementId: return p.AsElementId().GetValue();
                default: return null;
            }
        }

        private static string SafeName(Element e)
        {
            try { return e.Name; }
            catch { return null; }
        }

        private static string SafeTypeName(Document doc, Element e)
        {
            try
            {
                var typeId = e.GetTypeId();
                if (typeId == null || typeId == ElementId.InvalidElementId)
                    return null;
                return (doc.GetElement(typeId) as ElementType)?.Name;
            }
            catch { return null; }
        }


        /// <summary>
        /// Filtre de categorie. Par defaut par sous-chaine — pratique, mais
        /// « Ossature » ramene aussi « Etiquettes d'ossature », ce qui fausse
        /// un comptage. ExactCategory impose l'egalite stricte.
        /// </summary>
        private bool CorrespondCategorie(string nomCategorie)
        {
            if (string.IsNullOrEmpty(Category))
                return true;
            if (nomCategorie == null)
                return false;
            return ExactCategory
                ? string.Equals(nomCategorie, Category, StringComparison.OrdinalIgnoreCase)
                : nomCategorie.IndexOf(Category, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string GetName()
        {
            return "ExportElementsTable";
        }
    }
}

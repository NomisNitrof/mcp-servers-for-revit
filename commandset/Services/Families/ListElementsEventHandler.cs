using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Énumère les éléments placés d'un document — l'hôte ou un modèle LIÉ
    /// (LinkId). Deux modes : inventaire chiffré par catégorie (Summarize) ou
    /// liste paginée d'éléments. Lecture seule.
    ///
    /// Remplace ai_element_filter côté amont, qui échoue systématiquement
    /// (NullReferenceException sur toutes les catégories, constaté 2026-08-26).
    /// </summary>
    public class ListElementsEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long? LinkId { get; set; }
        public string Category { get; set; }
        public bool ExactCategory { get; set; }
        public bool Summarize { get; set; }
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

                // Seuls les éléments placés (pas les types) : c'est ce qui se
                // compte dans une quantité.
                var elements = new FilteredElementCollector(doc)
                    .WhereElementIsNotElementType()
                    .Where(e => e.Category != null)
                    .Where(e => CorrespondCategorie(e.Category.Name))
                    .ToList();

                if (Summarize)
                {
                    // Inventaire chiffré. Sans filtre de catégorie : une ligne
                    // par catégorie. Avec filtre : détail par type, ce qui est
                    // la granularité utile pour une quantité.
                    if (string.IsNullOrEmpty(Category))
                    {
                        var parCategorie = elements
                            .GroupBy(e => e.Category.Name)
                            .Select(g => new { category = g.Key, count = g.Count() })
                            .OrderByDescending(x => x.count)
                            .Take(Limit)
                            .ToList();

                        Result = new
                        {
                            documentTitle = doc.Title,
                            linkId = LinkId,
                            totalElements = elements.Count,
                            categoryCount = elements.Select(e => e.Category.Name).Distinct().Count(),
                            returned = parCategorie.Count,
                            categories = parCategorie
                        };
                    }
                    else
                    {
                        var parType = elements
                            .GroupBy(e => new
                            {
                                Categorie = e.Category.Name,
                                Type = SafeTypeName(doc, e)
                            })
                            .Select(g => new
                            {
                                category = g.Key.Categorie,
                                typeName = g.Key.Type,
                                count = g.Count()
                            })
                            .OrderByDescending(x => x.count)
                            .Take(Limit)
                            .ToList();

                        Result = new
                        {
                            documentTitle = doc.Title,
                            linkId = LinkId,
                            categoryFilter = Category,
                            totalElements = elements.Count,
                            returned = parType.Count,
                            types = parType
                        };
                    }
                }
                else
                {
                    var page = elements
                        .OrderBy(e => e.Id.GetValue())
                        .Skip(Offset)
                        .Take(Limit)
                        .Select(e => new
                        {
                            elementId = e.Id.GetValue(),
                            name = SafeName(e),
                            category = e.Category.Name,
                            typeId = e.GetTypeId()?.GetValue(),
                            typeName = SafeTypeName(doc, e)
                        })
                        .ToList();

                    Result = new
                    {
                        documentTitle = doc.Title,
                        linkId = LinkId,
                        categoryFilter = Category,
                        total = elements.Count,
                        offset = Offset,
                        returned = page.Count,
                        hasMore = Offset + page.Count < elements.Count,
                        elements = page
                    };
                }
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

        // Certains éléments lèvent sur .Name : ne jamais faire échouer un
        // inventaire entier pour un élément récalcitrant.
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
                if (typeId == null || typeId == Autodesk.Revit.DB.ElementId.InvalidElementId)
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
            return "ListElements";
        }
    }
}

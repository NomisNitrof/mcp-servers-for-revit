using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Liste les FAMILLES chargeables du document de projet (l'élément Family
    /// lui-même, avec son id — introuvable via les outils de types). Filtrable
    /// par sous-chaîne de nom et par catégorie ; limité pour rester sous le
    /// plafond ~8 Ko du canal socket (décision D4).
    /// </summary>
    public class ListFamiliesEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        /// <summary>Id d'une occurrence de lien (list_links) pour lire DANS ce lien ; null = document hôte.</summary>
        public long? LinkId { get; set; }
        public string NameFilter { get; set; }
        public string CategoryFilter { get; set; }
        public int Limit { get; set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 10000)
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
                // Document cible : l'hôte, ou le document d'un lien si LinkId
                // est fourni (lecture seule — voir LinkUtils).
                if (!LinkUtils.TryResolveDocument(app, LinkId, out Document doc, out string erreurDoc))
                {
                    ErrorMessage = erreurDoc;
                    return;
                }

                var familles = new FilteredElementCollector(doc)
                    .OfClass(typeof(Family))
                    .Cast<Family>()
                    .Where(f => string.IsNullOrEmpty(NameFilter) ||
                                f.Name.IndexOf(NameFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                    .Where(f => string.IsNullOrEmpty(CategoryFilter) ||
                                (f.FamilyCategory?.Name?.IndexOf(CategoryFilter, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderBy(f => f.Name, StringComparer.Ordinal)
                    .ToList();

                var page = familles
                    .Take(Limit)
                    .Select(f => new
                    {
                        familyId = f.Id.GetValue(),
                        name = f.Name,
                        category = f.FamilyCategory?.Name,
                        isEditable = f.IsEditable,
                        isInPlace = f.IsInPlace,
                        typeCount = f.GetFamilySymbolIds().Count
                    })
                    .ToList();

                Result = new
                {
                    total = familles.Count,
                    returned = page.Count,
                    hasMore = page.Count < familles.Count,
                    families = page
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
            return "ListFamilies";
        }
    }
}

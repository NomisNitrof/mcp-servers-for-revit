using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Liste (paginée) les paramètres d'un type — FamilySymbol ou type système
    /// (ElementType) — dans le document de projet. Lecture seule, pas de transaction.
    /// Pagination pour rester sous le plafond ~8 Ko du canal socket (décision D4).
    /// </summary>
    public class ListFamilyTypeParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        /// <summary>Id d'une occurrence de lien (list_links) pour lire DANS ce lien ; null = document hôte.</summary>
        public long? LinkId { get; set; }
        public long TypeId { get; set; }
        public int Offset { get; set; }
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

                var elementType = doc.GetElement(FamilyParameterUtils.IdFromValue(TypeId)) as ElementType;
                if (elementType == null)
                {
                    ErrorMessage = $"aucun type (ElementType) avec l'id {TypeId}";
                    return;
                }

                // Ordre stable (nom puis id) pour que la pagination soit cohérente entre appels
                var ordered = elementType.Parameters
                    .Cast<Parameter>()
                    .OrderBy(p => p.Definition?.Name ?? "", StringComparer.Ordinal)
                    .ThenBy(p => p.Id.GetValue())
                    .ToList();

                var page = ordered
                    .Skip(Offset)
                    .Take(Limit)
                    .Select(p => new FamilyTypeParameterInfo
                    {
                        Id = p.Id.GetValue(),
                        Name = p.Definition?.Name,
                        StorageType = p.StorageType.ToString(),
                        IsReadOnly = p.IsReadOnly,
                        Value = ExtractValue(p),
                        Display = p.StorageType == StorageType.Double ? p.AsValueString() : null
                    })
                    .ToList();

                Result = new
                {
                    typeId = TypeId,
                    typeName = elementType.Name,
                    familyName = elementType.FamilyName,
                    total = ordered.Count,
                    offset = Offset,
                    returned = page.Count,
                    hasMore = Offset + page.Count < ordered.Count,
                    parameters = page
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

        private static object ExtractValue(Parameter p)
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

        public string GetName()
        {
            return "ListFamilyTypeParameters";
        }
    }
}

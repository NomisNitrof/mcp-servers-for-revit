using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Liste (paginée) les paramètres d'OCCURRENCE d'un élément placé dans le
    /// document de projet — le pendant instance de ListFamilyTypeParameters.
    /// Lecture seule, pas de transaction. Pagination pour rester sous le
    /// plafond ~8 Ko du canal socket (décision D4).
    /// </summary>
    public class ListElementParametersEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public long ElementId { get; set; }
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
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    ErrorMessage = "aucun document actif dans Revit";
                    return;
                }

                var element = doc.GetElement(FamilyParameterUtils.IdFromValue(ElementId));
                if (element == null)
                {
                    ErrorMessage = $"aucun élément avec l'id {ElementId}";
                    return;
                }
                if (element is ElementType)
                {
                    ErrorMessage = $"l'id {ElementId} est un TYPE — utiliser list_family_type_parameters " +
                                   "pour les paramètres de type";
                    return;
                }

                // Ordre stable (nom puis id) pour que la pagination soit cohérente entre appels
                var ordered = element.Parameters
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
                    elementId = ElementId,
                    name = element.Name,
                    category = element.Category?.Name,
                    typeId = element.GetTypeId()?.GetValue(),
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
            return "ListElementParameters";
        }
    }
}

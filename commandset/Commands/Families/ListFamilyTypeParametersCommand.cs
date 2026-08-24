using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// list_family_type_parameters — liste paginée des paramètres d'un type
    /// (FamilySymbol ou type système) du document de projet.
    /// Pagination (offset/limit) pour rester sous ~8 Ko par réponse (décision D4).
    /// </summary>
    public class ListFamilyTypeParametersCommand : ExternalEventCommandBase
    {
        private const int DefaultLimit = 30;
        private const int MaxLimit = 100;

        private static readonly object _executionLock = new object();
        private ListFamilyTypeParametersEventHandler _handler => (ListFamilyTypeParametersEventHandler)Handler;

        public override string CommandName => "list_family_type_parameters";

        public ListFamilyTypeParametersCommand(UIApplication uiApp)
            : base(new ListFamilyTypeParametersEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    long? typeId = parameters?["typeId"]?.Value<long>();
                    if (!typeId.HasValue)
                        throw new ArgumentException("typeId est obligatoire");

                    int offset = parameters?["offset"]?.Value<int>() ?? 0;
                    int limit = parameters?["limit"]?.Value<int>() ?? DefaultLimit;
                    if (offset < 0)
                        offset = 0;
                    if (limit < 1)
                        limit = 1;
                    if (limit > MaxLimit)
                        limit = MaxLimit;

                    _handler.TypeId = typeId.Value;
                    _handler.Offset = offset;
                    _handler.Limit = limit;

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("list_family_type_parameters : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"list_family_type_parameters a échoué : {ex.Message}");
                }
            }
        }
    }
}

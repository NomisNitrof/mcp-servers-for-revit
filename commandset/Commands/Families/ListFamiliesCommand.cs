using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// list_families — liste les familles chargeables du projet avec leur
    /// familyId (le point d'entrée de edit_family_add_type), filtrable par
    /// nom et catégorie, limité (~8 Ko par réponse, décision D4).
    /// </summary>
    public class ListFamiliesCommand : ExternalEventCommandBase
    {
        private const int DefaultLimit = 50;
        private const int MaxLimit = 200;

        private static readonly object _executionLock = new object();
        private ListFamiliesEventHandler _handler => (ListFamiliesEventHandler)Handler;

        public override string CommandName => "list_families";

        public ListFamiliesCommand(UIApplication uiApp)
            : base(new ListFamiliesEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    int limit = parameters?["limit"]?.Value<int>() ?? DefaultLimit;
                    if (limit < 1)
                        limit = 1;
                    if (limit > MaxLimit)
                        limit = MaxLimit;

                    _handler.NameFilter = parameters?["nameFilter"]?.Value<string>();
                    _handler.CategoryFilter = parameters?["categoryFilter"]?.Value<string>();
                    _handler.Limit = limit;

                    if (!RaiseAndWaitForCompletion(15000))
                        throw new TimeoutException("list_families : délai dépassé (15 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"list_families a échoué : {ex.Message}");
                }
            }
        }
    }
}

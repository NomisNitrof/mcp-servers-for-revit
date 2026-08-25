using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Services.Families;
using RevitMCPSDK.API.Base;

namespace RevitMCPCommandSet.Commands.Families
{
    /// <summary>
    /// edit_family_add_type — spike palier B (décision D3) : EditFamily →
    /// FamilyManager.NewType + Set sur le document de famille → LoadFamily →
    /// Close garanti. Timeout étendu à 60 s (décision D4) : EditFamily +
    /// LoadFamily sur une famille lourde dépasse facilement les 10 s par défaut.
    /// </summary>
    public class EditFamilyAddTypeCommand : ExternalEventCommandBase
    {
        /// <summary>Timeout étendu par commande (décision D4).</summary>
        private const int TimeoutMs = 60000;

        private static readonly object _executionLock = new object();
        private EditFamilyAddTypeEventHandler _handler => (EditFamilyAddTypeEventHandler)Handler;

        public override string CommandName => "edit_family_add_type";

        public EditFamilyAddTypeCommand(UIApplication uiApp)
            : base(new EditFamilyAddTypeEventHandler(), uiApp)
        {
        }

        public override object Execute(JObject parameters, string requestId)
        {
            lock (_executionLock)
            {
                try
                {
                    long? familyId = parameters?["familyId"]?.Value<long>();
                    long? typeId = parameters?["typeId"]?.Value<long>();
                    if (!familyId.HasValue && !typeId.HasValue)
                        throw new ArgumentException("familyId ou typeId est obligatoire");

                    string newTypeName = parameters?["newTypeName"]?.Value<string>();
                    if (string.IsNullOrWhiteSpace(newTypeName))
                        throw new ArgumentException("newTypeName est obligatoire et non vide");

                    _handler.FamilyId = familyId;
                    _handler.TypeId = typeId;
                    _handler.NewTypeName = newTypeName;
                    _handler.Parameters = parameters?["parameters"]?.ToObject<List<ParameterValueSpec>>();

                    if (!RaiseAndWaitForCompletion(TimeoutMs))
                        throw new TimeoutException("edit_family_add_type : délai dépassé (60 s)");

                    if (!_handler.IsSuccess)
                        throw new Exception(_handler.ErrorMessage ?? "échec sans message");

                    return _handler.Result;
                }
                catch (Exception ex)
                {
                    throw new Exception($"edit_family_add_type a échoué : {ex.Message}");
                }
            }
        }
    }
}

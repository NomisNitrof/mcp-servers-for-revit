using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Charge un fichier .rfa du disque dans le document de projet
    /// (Document.LoadFamily avec écrasement si la famille existe déjà).
    /// Timeout étendu : un .rfa lourd peut dépasser les 10 s par défaut.
    /// </summary>
    public class LoadFamilyEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée
        public string Path { get; set; }

        public bool WaitForCompletion(int timeoutMilliseconds = 60000)
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

                if (!System.IO.File.Exists(Path))
                {
                    ErrorMessage = $"fichier introuvable : {Path}";
                    return;
                }
                if (!Path.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase))
                {
                    ErrorMessage = $"pas un fichier de famille .rfa : {Path}";
                    return;
                }

                Family family;
                using (var transaction = new Transaction(doc, "MCP — Charger une famille"))
                {
                    transaction.Start();
                    bool loaded = doc.LoadFamily(Path, new OverwriteFamilyLoadOptions(), out family);
                    transaction.Commit();
                    if (!loaded && family == null)
                    {
                        ErrorMessage = "LoadFamily a renvoyé false — famille identique déjà chargée, " +
                                       "ou fichier refusé par Revit";
                        return;
                    }
                }

                var types = family.GetFamilySymbolIds()
                    .Take(20)
                    .Select(id => new
                    {
                        typeId = id.GetValue(),
                        name = (doc.GetElement(id) as FamilySymbol)?.Name
                    })
                    .ToList();

                Result = new
                {
                    familyId = family.Id.GetValue(),
                    familyName = family.Name,
                    category = family.FamilyCategory?.Name,
                    typeCount = family.GetFamilySymbolIds().Count,
                    types
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
            return "LoadFamily";
        }
    }
}

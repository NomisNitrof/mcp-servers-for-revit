using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitMCPCommandSet.Models.Families;
using RevitMCPCommandSet.Utils;
using RevitMCPSDK.API.Interfaces;

namespace RevitMCPCommandSet.Services.Families
{
    /// <summary>
    /// Spike palier B (décision D3) : vraie édition de famille.
    /// Séquence : EditFamily → transaction sur le document de FAMILLE
    /// (FamilyManager.NewType + Set) → LoadFamily vers le projet → Close.
    /// Le document de famille est fermé de façon GARANTIE (finally), même en
    /// cas d'erreur, pour ne pas laisser de documents fantômes en mémoire.
    /// Inéligible pour les familles système (IsEditable == false).
    /// </summary>
    public class EditFamilyAddTypeEventHandler : IExternalEventHandler, IWaitableExternalEventHandler
    {
        public bool IsSuccess { get; private set; }
        public string ErrorMessage { get; private set; }
        public object Result { get; private set; }

        private readonly ManualResetEvent _resetEvent = new ManualResetEvent(false);

        // Paramètres d'entrée — FamilyId (Family) ou TypeId (FamilySymbol,
        // résolu en famille via symbol.Family) ; au moins un des deux.
        public long? FamilyId { get; set; }
        public long? TypeId { get; set; }
        public string NewTypeName { get; set; }
        public List<ParameterValueSpec> Parameters { get; set; }

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
            Document famDoc = null;
            try
            {
                var doc = app.ActiveUIDocument?.Document;
                if (doc == null)
                {
                    ErrorMessage = "aucun document actif dans Revit";
                    return;
                }

                Family family = null;
                if (FamilyId.HasValue)
                {
                    family = doc.GetElement(FamilyParameterUtils.IdFromValue(FamilyId.Value)) as Family;
                    if (family == null)
                    {
                        ErrorMessage = $"aucune famille (Family) avec l'id {FamilyId}";
                        return;
                    }
                }
                else
                {
                    var symbol = doc.GetElement(FamilyParameterUtils.IdFromValue(TypeId.Value)) as FamilySymbol;
                    if (symbol == null)
                    {
                        ErrorMessage = $"aucun type de famille chargeable (FamilySymbol) avec l'id {TypeId} — " +
                                       "pour un type système, utiliser duplicate_family_type";
                        return;
                    }
                    family = symbol.Family;
                }

                if (!family.IsEditable)
                {
                    ErrorMessage = "famille non éditable par EditFamily (famille système ?) — " +
                                   "utiliser duplicate_family_type pour les types système";
                    return;
                }

                // Capturés AVANT LoadFamily : le rechargement (overwrite)
                // invalide la référence `family` d'origine.
                string familyName = family.Name;
                long familyIdValue = family.Id.GetValue();
                var appliedInfos = new List<AppliedParameterInfo>();
                var skipped = new List<SkippedParameterInfo>();

                // 1. Ouvre le document de famille en mémoire (jamais pendant une
                //    transaction ouverte sur le document de projet).
                famDoc = doc.EditFamily(family);

                // 2. Transaction sur le document de FAMILLE.
                var familyManager = famDoc.FamilyManager;
                using (var transaction = new Transaction(famDoc, "MCP — Ajouter un type de famille"))
                {
                    transaction.Start();

                    FamilyType newType = familyManager.NewType(NewTypeName);
                    familyManager.CurrentType = newType;

                    if (Parameters != null)
                    {
                        foreach (var spec in Parameters)
                        {
                            string label = spec.Name ?? spec.Id?.ToString() ?? "(sans nom ni id)";
                            if (spec.Value == null)
                            {
                                skipped.Add(new SkippedParameterInfo { Name = label, Reason = "valeur absente" });
                                continue;
                            }

                            var fp = FamilyParameterUtils.FindFamilyParameter(familyManager, spec);
                            if (fp == null)
                            {
                                skipped.Add(new SkippedParameterInfo
                                {
                                    Name = label,
                                    Reason = "paramètre introuvable dans la famille"
                                });
                                continue;
                            }

                            if (FamilyParameterUtils.TrySetFamilyParameter(familyManager, fp, spec.Value, out string reason))
                            {
                                appliedInfos.Add(new AppliedParameterInfo
                                {
                                    Id = fp.Id.GetValue(),
                                    Name = fp.Definition?.Name
                                });
                            }
                            else
                            {
                                skipped.Add(new SkippedParameterInfo
                                {
                                    Name = fp.Definition?.Name ?? label,
                                    Reason = reason
                                });
                            }
                        }
                    }

                    transaction.Commit();
                }

                // 3. Recharge la famille dans le projet (écrase les valeurs de
                //    paramètres existantes — comportement assumé du spike).
                // LoadFamily peut lever une exception (« referenced object is
                // not valid ») APRÈS avoir réellement rechargé la famille : on
                // ne conclut à l'échec qu'après vérification de l'état réel.
                Family loadedFamily = null;
                string warning = null;
                Exception erreurLoad = null;
                try
                {
                    loadedFamily = famDoc.LoadFamily(doc, new OverwriteFamilyLoadOptions());
                }
                catch (Exception exLoad)
                {
                    erreurLoad = exLoad;
                }

                // Retrouve l'id du nouveau FamilySymbol côté projet. La Family
                // renvoyée par LoadFamily (comme celle d'origine) peut être
                // invalidée par le rechargement avec écrasement : re-résolution
                // depuis le projet par l'id capturé.
                long? newTypeId = null;
                try
                {
                    var familleProjet =
                        doc.GetElement(FamilyParameterUtils.IdFromValue(familyIdValue)) as Family
                        ?? loadedFamily;
                    if (familleProjet != null)
                    {
                        foreach (ElementId symbolId in familleProjet.GetFamilySymbolIds())
                        {
                            if (doc.GetElement(symbolId) is FamilySymbol symbol && symbol.Name == NewTypeName)
                            {
                                newTypeId = symbolId.GetValue();
                                break;
                            }
                        }
                    }
                }
                catch (Exception exRelecture)
                {
                    warning = "type créé et famille rechargée, mais relecture du nouveau type impossible : " +
                              exRelecture.Message;
                }

                if (erreurLoad != null)
                {
                    if (newTypeId.HasValue)
                    {
                        // Le nouveau type est bien dans le projet : succès réel,
                        // l'exception de LoadFamily n'était que du bruit.
                        warning = $"LoadFamily a levé « {erreurLoad.Message} » mais le nouveau type " +
                                  "est présent dans le projet — opération considérée réussie";
                    }
                    else
                    {
                        ErrorMessage = $"LoadFamily a échoué (étape rechargement) : {erreurLoad.Message}";
                        return;
                    }
                }

                Result = new
                {
                    familyId = familyIdValue,
                    familyName,
                    newTypeName = NewTypeName,
                    newTypeId,
                    warning,
                    applied = appliedInfos,
                    skipped
                };
                IsSuccess = true;
            }
            catch (Exception ex)
            {
                ErrorMessage = ex.Message;
            }
            finally
            {
                // 4. Fermeture GARANTIE du document de famille, succès ou échec.
                if (famDoc != null)
                {
                    try
                    {
                        famDoc.Close(false);
                    }
                    catch
                    {
                        // Document déjà fermé ou invalide — rien à faire de plus.
                    }
                }

                _resetEvent.Set();
            }
        }

        public string GetName()
        {
            return "EditFamilyAddType";
        }
    }

    /// <summary>
    /// IFamilyLoadOptions du spike : recharge toujours la famille et écrase les
    /// valeurs de paramètres du projet par celles du document de famille.
    /// </summary>
    public class OverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(
            Family sharedFamily,
            bool familyInUse,
            out FamilySource source,
            out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}

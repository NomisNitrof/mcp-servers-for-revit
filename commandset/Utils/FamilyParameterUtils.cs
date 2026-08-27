using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitMCPCommandSet.Models.Families;

namespace RevitMCPCommandSet.Utils
{
    /// <summary>
    /// Helpers partagés par les commandes familles : construction d'ElementId
    /// multi-versions, résolution et écriture de paramètres — côté document de
    /// projet (Parameter) et côté éditeur de familles (FamilyParameter).
    /// </summary>
    public static class FamilyParameterUtils
    {
        /// <summary>Construit un ElementId depuis une valeur numérique, toutes versions Revit.</summary>
        public static ElementId IdFromValue(long value)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(value);
#else
            return new ElementId((int)value);
#endif
        }

        /// <summary>
        /// Valeur AFFICHABLE d'un parametre : chaine formatee pour les doubles,
        /// et NOM de l'element reference pour les ElementId — un id brut
        /// (« Niveau de reference = 60506 ») n'a aucun sens dans une table.
        /// null quand il n'y a rien de plus lisible que la valeur brute.
        /// </summary>
        public static string AffichageParametre(Document doc, Parameter p)
        {
            try
            {
                if (p == null || !p.HasValue)
                    return null;
                switch (p.StorageType)
                {
                    case StorageType.Double:
                        return p.AsValueString();
                    case StorageType.ElementId:
                        var id = p.AsElementId();
                        if (id == null || id == ElementId.InvalidElementId)
                            return null;
                        return doc?.GetElement(id)?.Name;
                    default:
                        return null;
                }
            }
            catch
            {
                return null;
            }
        }

        // ------------------------------------------------------------------
        // Document de projet : Parameter sur un ElementType
        // ------------------------------------------------------------------

        /// <summary>Trouve un paramètre sur un élément par id ou par nom (l'id gagne).</summary>
        public static Parameter FindParameter(Element element, ParameterValueSpec spec)
        {
            if (spec.Id.HasValue)
            {
                foreach (Parameter p in element.Parameters)
                {
                    if (p.Id.GetValue() == spec.Id.Value)
                        return p;
                }
                return null;
            }

            return string.IsNullOrEmpty(spec.Name) ? null : element.LookupParameter(spec.Name);
        }

        /// <summary>
        /// Écrit une valeur dans un Parameter du document de projet.
        /// Doubles : nombre JSON = unités internes Revit ; chaîne JSON = unités
        /// d'affichage du projet (SetValueString). Booléens acceptés pour les
        /// paramètres entiers (Oui/Non).
        /// </summary>
        public static bool TrySetParameter(Parameter param, JToken value, out string reason)
        {
            reason = null;
            if (param.IsReadOnly)
            {
                reason = "paramètre en lecture seule";
                return false;
            }

            try
            {
                bool ok;
                switch (param.StorageType)
                {
                    case StorageType.String:
                        ok = param.Set(value.Value<string>());
                        break;
                    case StorageType.Integer:
                        ok = value.Type == JTokenType.Boolean
                            ? param.Set(value.Value<bool>() ? 1 : 0)
                            : param.Set(value.Value<int>());
                        break;
                    case StorageType.Double:
                        ok = value.Type == JTokenType.String
                            ? param.SetValueString(value.Value<string>())
                            : param.Set(value.Value<double>());
                        break;
                    case StorageType.ElementId:
                        ok = param.Set(IdFromValue(value.Value<long>()));
                        break;
                    default:
                        reason = $"storageType non géré ({param.StorageType})";
                        return false;
                }

                if (!ok)
                    reason = "valeur refusée par Revit (Set a renvoyé false)";
                return ok;
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Applique une liste de specs sur un élément ; remplit applied/skipped
        /// (aucun échec silencieux : chaque spec finit dans l'une des deux listes).
        /// À appeler à l'intérieur d'une transaction ouverte.
        /// </summary>
        public static void ApplySpecs(
            Element target,
            List<ParameterValueSpec> specs,
            List<AppliedParameterInfo> applied,
            List<SkippedParameterInfo> skipped)
        {
            if (specs == null)
                return;

            foreach (var spec in specs)
            {
                string label = spec.Name ?? spec.Id?.ToString() ?? "(sans nom ni id)";
                if (spec.Value == null || spec.Value.Type == JTokenType.Null)
                {
                    skipped.Add(new SkippedParameterInfo { Name = label, Reason = "valeur absente" });
                    continue;
                }

                var param = FindParameter(target, spec);
                if (param == null)
                {
                    skipped.Add(new SkippedParameterInfo { Name = label, Reason = "paramètre introuvable sur le type" });
                    continue;
                }

                if (TrySetParameter(param, spec.Value, out string reason))
                    applied.Add(new AppliedParameterInfo { Id = param.Id.GetValue(), Name = param.Definition?.Name });
                else
                    skipped.Add(new SkippedParameterInfo { Name = param.Definition?.Name ?? label, Reason = reason });
            }
        }

        // ------------------------------------------------------------------
        // Éditeur de familles : FamilyParameter via FamilyManager
        // ------------------------------------------------------------------

        /// <summary>Trouve un FamilyParameter par id ou par nom (l'id gagne).</summary>
        public static FamilyParameter FindFamilyParameter(FamilyManager familyManager, ParameterValueSpec spec)
        {
            foreach (FamilyParameter fp in familyManager.Parameters)
            {
                if (spec.Id.HasValue)
                {
                    if (fp.Id.GetValue() == spec.Id.Value)
                        return fp;
                }
                else if (!string.IsNullOrEmpty(spec.Name) && fp.Definition?.Name == spec.Name)
                {
                    return fp;
                }
            }

            return null;
        }

        /// <summary>
        /// Écrit une valeur dans un FamilyParameter du type courant du FamilyManager.
        /// Doubles en unités internes Revit (pas de SetValueString côté FamilyManager).
        /// </summary>
        public static bool TrySetFamilyParameter(
            FamilyManager familyManager,
            FamilyParameter fp,
            JToken value,
            out string reason)
        {
            reason = null;
            if (fp.IsDeterminedByFormula)
            {
                reason = "paramètre piloté par une formule";
                return false;
            }

            if (fp.IsReadOnly)
            {
                reason = "paramètre en lecture seule";
                return false;
            }

            try
            {
                switch (fp.StorageType)
                {
                    case StorageType.String:
                        familyManager.Set(fp, value.Value<string>());
                        return true;
                    case StorageType.Integer:
                        familyManager.Set(fp, value.Type == JTokenType.Boolean
                            ? (value.Value<bool>() ? 1 : 0)
                            : value.Value<int>());
                        return true;
                    case StorageType.Double:
                        familyManager.Set(fp, value.Value<double>());
                        return true;
                    case StorageType.ElementId:
                        familyManager.Set(fp, IdFromValue(value.Value<long>()));
                        return true;
                    default:
                        reason = $"storageType non géré ({fp.StorageType})";
                        return false;
                }
            }
            catch (Exception ex)
            {
                reason = ex.Message;
                return false;
            }
        }
    }
}

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitMCPCommandSet.Models.Families
{
    /// <summary>
    /// Paramètre à écrire : ciblé par nom OU par id (l'id gagne si les deux
    /// sont fournis), avec sa nouvelle valeur.
    /// </summary>
    public class ParameterValueSpec
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("id")]
        public long? Id { get; set; }

        [JsonProperty("value")]
        public JToken Value { get; set; }
    }

    /// <summary>Paramètre effectivement écrit.</summary>
    public class AppliedParameterInfo
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }
    }

    /// <summary>
    /// Paramètre ignoré (introuvable, lecture seule, valeur refusée...) —
    /// toujours renvoyé au client, jamais d'échec silencieux.
    /// </summary>
    public class SkippedParameterInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }

    /// <summary>Description compacte d'un paramètre de type (réponse paginée).</summary>
    public class FamilyTypeParameterInfo
    {
        [JsonProperty("id")]
        public long Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("storageType")]
        public string StorageType { get; set; }

        [JsonProperty("isReadOnly")]
        public bool IsReadOnly { get; set; }

        /// <summary>Valeur brute : double en unités internes Revit, entier, chaîne, ou id d'élément.</summary>
        [JsonProperty("value")]
        public object Value { get; set; }

        /// <summary>Valeur formatée en unités d'affichage (doubles seulement).</summary>
        [JsonProperty("display", NullValueHandling = NullValueHandling.Ignore)]
        public string Display { get; set; }
    }
}

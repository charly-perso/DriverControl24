# Instructions Copilot — DriverControl24

## Langage et style
- Tout le code produit est en **VB.NET** (sauf les fichiers `ReaperC24Bridge/*.cs` qui restent en C#)
- Commentaires XML sur toutes les propriétés et méthodes publiques
- `CultureInfo.InvariantCulture` obligatoire pour tout `Double.ToString` / `Double.Parse` sur le pipe
- Pas de `StringBuilder` (NativeAOT — utiliser `String.Join` ou interpolation)
- Pas de réflexion (`System.Reflection`) — NativeAOT-safe uniquement
- Pas de ?? 

## Architecture pipe
- Commandes pipe : format `COMMANDE|param1|param2|...` en ANSI, terminées par `\n`
- Réponses : valeur brute ou `ERROR:message`
- Séparateur `|` — ne jamais utiliser `,` pour les valeurs numériques
- `Double` toujours en `InvariantCulture` ("G" format)

## Patterns à respecter
- Les événements de changement suivent le pattern : `Public Event On_XxxChange(value As T)`
- La détection de changement dans `Refresh()` utilise le pattern : `Dim new_x = Parse...(p(n)) : If _x <> new_x Then : _x = new_x : RaiseEvent On_XxxChange(_x) : End If`
- Les setters de propriété suivent : `_field = Value : SetD/SetB/SetI("PARAM", Value)`
- `SetD` / `SetB` / `SetI` sont les seuls points d'écriture vers Reaper via pipe

## Projets de la solution
| Projet | Rôle | Langage |
|--------|------|---------|
| `ReaperC24Bridge` | Plugin NativeAOT chargé par Reaper | C# |
| `DriverControl24.Extension` | Bridge + API Reaper (VB) | VB.NET |
| `DriverReaperControl24` | Driver hardware Control24 | VB.NET |

## Ne pas faire
- Ne pas utiliser `Task.Delay` dans les hot paths réseau
- Ne pas ouvrir le pipe plus d'une fois par requête (pas de connexion persistante côté driver)
- Ne pas utiliser `Marshal.StringToHGlobalAnsi` côté driver (uniquement côté plugin)
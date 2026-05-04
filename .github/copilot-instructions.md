# Instructions Copilot – DriverControl24

## Langage et style
- Tout le code produit est en **VB.NET** (sauf les fichiers `ReaperC24Bridge/*.cs` qui restent en C#)
- Commentaires XML sur toutes les propriétés et méthodes publiques
- `CultureInfo.InvariantCulture` obligatoire pour tout `Double.ToString` / `Double.Parse` sur le pipe
- Pas de `StringBuilder` (NativeAOT – utiliser `String.Join` ou interpolation)
- Pas de réflexion (`System.Reflection`) – NativeAOT-safe uniquement
- Pas de ?? 
- En VB.NET, ne jamais utiliser la notation inline. Toujours utiliser la notation multi-ligne complète pour : If/Then/End If, propriétés (Get/Set/End Get/End Set), lambdas, boucles, etc. Exemples interdits : `If x Then y`, `Return _x : End Get`, `Sub(v) : ... : End Sub`. Toujours écrire le bloc complet sur plusieurs lignes.
- En VB.NET, ne jamais utiliser le format compact (inline) pour les propriétés. Chaque mot-clé (Get, End Get, Set, End Set) doit être sur sa propre ligne. Ne jamais écrire Get/Set/End Get/End Set sur la même ligne que le code.

## Architecture pipe
- Commandes pipe : format `COMMANDE|param1|param2|...` en ANSI, terminées par `\n`
- Réponses : valeur brute ou `ERROR:message`
- Séparateur `|` – ne jamais utiliser `,` pour les valeurs numériques
- `Double` toujours en `InvariantCulture` ("G" format)

## Patterns à respecter
- Les événements de changement suivent le pattern : `Public Event On_XxxChange(value As T)`
- La détection de changement dans `Refresh()` utilise le pattern : 
```vb
Dim new_x = Parse...(p(n)) 
If _x <> new_x Then 
    _x = new_x 
    RaiseEvent On_XxxChange(_x) 
End If
```
- Les setters de propriété suivent : 
```vb
_field = Value 
SetD/SetB/SetI("PARAM", Value)
```
- `SetD` / `SetB` / `SetI` sont les seuls points d'écriture vers Reaper via pipe
- Les propriétés VB.NET doivent être écrites en format entièrement développé (chaque mot-clé sur sa propre ligne) : 
```vb
Public Property Vol As Double
    Get
        Return _vol
    End Get
    Set
        _vol = Value
        SetD("D_VOL", Value)
    End Set
End Property
```
  Ne jamais utiliser le format compact avec `:` sur la même ligne que Get/Set/End Get/End Set.

## Gestion des Délégués
- En VB.NET, pour stocker des handlers de délégués utilisés avec `AddHandler`/`RemoveHandler`, utiliser `[Delegate]` comme type de variable et non `Action(Of T)` — `Action(Of T)` ne compile pas dans ce contexte. Exemple : `Dim volH As [Delegate] = Sub(v) ...` puis `AddHandler track.On_VolChange, volH` / `RemoveHandler track.On_VolChange, volH`.

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
- Ne pas générer des propriétés avec le format Get : Return : End Get Set : : End Set
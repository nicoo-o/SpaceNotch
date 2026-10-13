<!-- Écris en français ou en anglais. · Write in French or English. -->

## Ce qui change · What changes

<!-- Une phrase : le défaut corrigé ou la fonction ajoutée, et pour qui. · One sentence: the bug fixed or the feature added, and for whom. -->

## Pourquoi · Why

<!-- La cause, mesurée si possible ; le ticket ou le numéro de la feuille de route. · The cause, measured if possible; the issue or roadmap number. -->

## Preuves · Evidence

- [ ] `dotnet build SpaceNotch.sln -c Release -p:Platform=x64` sans avertissement · with no warning
- [ ] `dotnet test SpaceNotch.sln -c Release` vert · green
- [ ] Un changement visible a sa capture ; un comportement du cœur a son test · A visible change has a screenshot; a core behaviour has a test
- [ ] Les textes affichés passent par `Lang.T` (français et anglais) · Displayed text goes through `Lang.T` (French and English)

## À savoir · Good to know

<!-- Ce qui reste, les risques, ce qui demande une décision. · What remains, the risks, what needs a decision. -->

# Contribuer · Contributing

**Français** · [English](#english)

Merci de vouloir aider. SpaceNotch est gratuit et open source (MIT).

**Une question, une idée** : les [Discussions](https://github.com/nicoo-o/SpaceNotch/discussions).
**Un bogue** : un [ticket](https://github.com/nicoo-o/SpaceNotch/issues/new/choose), avec ta version de
Windows, l'échelle d'affichage et le passage utile du journal (`%LocalAppData%\SpaceNotch\logs`). Relis
le journal avant : il peut contenir des chemins de fichiers.

**Proposer une modification.**
1. Construis et teste comme dans [docs/development/building.md](docs/development/building.md) :
   `dotnet build SpaceNotch.sln -c Release -p:Platform=x64`, puis `dotnet test SpaceNotch.sln -c Release`.
   En Release, un avertissement est une erreur.
2. Une modification visible a sa capture, et un comportement du cœur a son test
   (`tests/SpaceNotch.Core.Tests`).
3. Code, commentaires, commits et descriptions de PR sont **en français**. Les textes affichés
   sont bilingues (`Lang.T("français", "English")`).
4. Une décision d'architecture s'écrit dans un ADR ([docs/decisions](docs/decisions/README.md)).
5. Les règles du projet sont dans [CLAUDE.md](CLAUDE.md) et
   [docs/ux/spacenotch-2.0.md](docs/ux/spacenotch-2.0.md) : les décisions verrouillées (§ 15) ne se
   rouvrent pas dans une PR.

En participant, tu acceptes le [code de conduite](CODE_OF_CONDUCT.md).

---

## English

Thanks for wanting to help. SpaceNotch is free and open source (MIT).

**A question, an idea**: [Discussions](https://github.com/nicoo-o/SpaceNotch/discussions).
**A bug**: an [issue](https://github.com/nicoo-o/SpaceNotch/issues/new/choose), with your Windows
version, display scale and the relevant part of the log (`%LocalAppData%\SpaceNotch\logs`). Read the
log first: it can contain file paths.

**Proposing a change.** Build and test as in [docs/development/building.md](docs/development/building.md).
Visible changes come with a screenshot, core behaviour with a test. The codebase, commits and pull
requests are written **in French**; on-screen text is bilingual. Architecture decisions are written as
ADRs. By taking part, you agree to the [code of conduct](CODE_OF_CONDUCT.md).

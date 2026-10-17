# CLAUDE.md

## Project

**Rackline** (working name, may change) is an ad-free web platform for running cue sports competitions: tournaments, leagues, live scoring, rankings and player records.

It is an independent, clean-room implementation of generic tournament-management functionality.

## Legal constraints (non-negotiable)

- Do **not** copy code, UI layouts, wording, icons or branding from any existing platform (including CueScore).
- Do **not** scrape, import or call third-party platforms' data or APIs.
- All seed and test data must be invented.
- No adverts, ad SDKs or third-party tracking scripts.
- UI text uses **British English** (e.g. "organiser", "colour", "cancelled").

## Stack

- .NET 10 (LTS), C#, nullable enabled, warnings as errors.
- ASP.NET Core with minimal API endpoints under `/api`.
- EF Core with Npgsql (PostgreSQL 16+).
- ASP.NET Core Identity for authentication.
- SignalR for live updates, with one group per tournament (`tournament:{id}`) and one per match (`match:{id}`).
- Blazor Web App for the frontend:
  - Public/spectator pages use **static SSR**: fast and indexable, with no Blazor circuit. They get live updates via the SignalR **JS client**.
  - Organiser/admin pages use Interactive Server render mode.
- xUnit and FluentAssertions for tests.

## Solution layout

```
src/
  Rackline.Domain/          # Entities, value objects, bracket/rating engines. NO external deps.
  Rackline.Application/     # Use cases, DTOs, interfaces (repositories, clock, notifier)
  Rackline.Infrastructure/  # EF Core DbContext, migrations, repositories, SignalR notifier
  Rackline.Web/             # Blazor Web App, API endpoints, SignalR hub, Identity
tests/
  Rackline.Domain.Tests/
  Rackline.Application.Tests/
  Rackline.Web.Tests/       # Integration tests (WebApplicationFactory)
```

Dependencies point inwards only: Web → Infrastructure → Application → Domain.

## Domain model (initial)

- **Organisation**: hierarchical (international → national → regional → club). Owns venues, tournaments and leagues.
- **Venue**, **Table**
- **Player**: profile, nationality, optional licence number, organisation memberships.
- **Discipline**: English 8-ball, US 8-ball, 9-ball, 10-ball, straight pool, snooker, heyball.
- **Tournament**
  - Discipline, format and race-to (or best-of).
  - Status: Draft → Registration → Drawn → InProgress → Completed / Cancelled.
- **Entry**: a player's registration in a tournament, with seed and handicap.
- **Match**
  - Round and bracket slot, two entries (either may be a bye), score and table.
  - State: Pending → Ready → Live → Completed / Walkover.
  - Links to the next match for the winner (and for the loser, in double elimination).
- **Frame**: optional per-frame result (snooker points, break-and-run flags).
- **League** → **Season** → **Team** / **Fixture** (team matches made of individual rubbers).
- **Ranking**: points table per organisation/discipline/season.
- **Rating**: Elo or Glicko-2 per player per discipline, updated on match completion.
- **Challenge**: an ad hoc match between two players, which counts towards ratings.

## Formats to support

1. Single elimination (any field size, with byes; standard seeding placement).
2. Double elimination (winners and losers brackets, optional grand-final reset).
3. Round robin (a single group, or multiple groups).
4. Groups followed by single-elimination knockout.
5. Swiss (later).

Bracket generation must be **pure, deterministic** domain code. Randomness is injected via a seeded `IRandom`. It must be exhaustively unit-tested, covering:
- field sizes 2–128, including non-powers of two;
- correct bye placement;
- seeds 1 and 2 placed in opposite halves;
- correct winner/loser progression;
- walkovers.

## Milestones (do in order; one at a time)

1. **M1 – Skeleton and domain**
   - Create the solution, projects and `Directory.Build.props` (nullable on, warnings as errors).
   - Build the domain entities and the single-elimination, double-elimination and round-robin engines.
   - Full unit tests.
   - No database yet.
2. **M2 – Persistence**
   - DbContext, configurations, migrations.
   - `docker-compose.yml` for Postgres.
   - Seed data (invented).
3. **M3 – API and auth**
   - Identity, with roles: Admin, Organiser, Referee, Player.
   - Endpoints for tournament CRUD, entries, seeding, the draw, and starting and finishing matches.
4. **M4 – Live scoring**
   - SignalR hub; score updates broadcast to groups.
   - Referee scoring screen.
5. **M5 – Public UI**
   - Tournament list, bracket view, live match page, player profile, head-to-head.
6. **M6 – Leagues**: seasons, teams, fixtures, tables.
7. **M7 – Rankings and ratings**
8. **M8 – Organisation hierarchy and permissions**
9. **M9 – Payments** (Stripe entry fees). Do not start without explicit instruction.
10. **M10 – Extras**
    - Public read-only API.
    - OBS streaming overlay page (transparent scoreboard).
    - Challenges.

## Conventions

- Prefer small, focused classes; no "manager" god-classes.
- Domain rules live in the Domain project, not in endpoints or Razor components.
- Use `TimeProvider` for time and never `DateTime.Now`. Store UTC.
- Validate at the boundary (endpoint/DTO). Domain methods throw on invariant breaches.
- EF Core:
  - Use `AsNoTracking()` for reads.
  - Project to DTOs with `Select`.
  - Avoid N+1 queries.
  - Add indexes for foreign keys and common filters.
- No secrets in the repo. Use user-secrets or environment variables.

## Working rules for Claude

- Work on **one milestone at a time**. Stop and summarise when it's done.
- Before each commit, run `dotnet build` and `dotnet test`. Both must pass.
- Make small, descriptive commits.
- Don't add NuGet packages beyond those named above without saying why.
- If a requirement is ambiguous, choose the simplest reasonable option and record it under **Decisions** below.

## Environment

- If `dotnet` is missing in the environment, install the .NET 10 SDK:

  ```
  curl -sSL https://dot.net/v1/dotnet-install.sh | bash /dev/stdin --channel 10.0
  export PATH="$HOME/.dotnet:$PATH"
  ```

- Integration tests needing Postgres/Docker may be skipped where Docker is unavailable. Mark them with a trait (`[Trait("Category","Integration")]`).

## Commands

```
dotnet tool restore                      # installs the local dotnet-ef tool
docker compose up -d                     # local Postgres 16
dotnet build
dotnet test
dotnet test --filter "Category!=Integration"
dotnet ef migrations add <Name> -p src/Rackline.Infrastructure -s src/Rackline.Web -o Persistence/Migrations
dotnet run --project src/Rackline.Web    # migrates and seeds in Development
```

Postgres integration tests run only when `RACKLINE_TEST_CONNECTION` is set; otherwise they are skipped. With docker-compose:

```
RACKLINE_TEST_CONNECTION="Host=localhost;Port=5432;Database=postgres;Username=rackline;Password=rackline_dev" dotnet test
```

On Windows PowerShell: `$env:RACKLINE_TEST_CONNECTION="..."` and then `dotnet test`.

To get a local administrator, set credentials once with user-secrets. They are used at startup in Development:

```
dotnet user-secrets set "Seed:AdminEmail" "you@example.com" --project src/Rackline.Web
dotnet user-secrets set "Seed:AdminPassword" "<at least 10 characters>" --project src/Rackline.Web
```

## Decisions

_(Append decisions here as they are made.)_

### M1

- **Tooling**
  - In the cloud sandbox `builds.dotnet.microsoft.com` is blocked, so the SDK was installed from Ubuntu's archive (`apt-get install dotnet-sdk-10.0`).
  - The solution uses the `.slnx` format, which is the .NET 10 default.
  - FluentAssertions is pinned to **7.x**: version 8 onwards needs a paid licence for commercial use.
  - The Web project comes from `dotnet new blazor --empty` (Interactive Server, set per page).
- **Match identity**
  - Matches link to each other by a per-tournament `Number`, not by `Guid`, so draws are fully deterministic.
  - `Id` (a v7 Guid) is used only for persistence.
- **Slots and byes**
  - Each slot is `Awaiting`, `Entry` or `Bye`.
  - When a match has an entry against a bye, it resolves itself as `Walkover` (`IsBye`).
  - When both slots are byes, the match is void (`IsVoid`) and passes a bye onwards.
  - The same mechanism handles single-elimination byes, byes in the losers' bracket and an unused grand-final reset.
- **Seeding**
  - Seeds use the standard placement (1, 8, 4, 5, 2, 7, 3, 6 …), so byes go to the top seeds.
  - Seeded entries come first in seed order. Unseeded entries are shuffled by `IRandom` and take the remaining seed positions.
  - At the draw, a seed may not be higher than the number of entries.
- **Double elimination**
  - The losers' bracket has 2(k−1) rounds.
  - Losers from each winners' round drop in, and the order is reversed on alternate rounds to avoid an immediate rematch.
  - The grand-final reset is optional and on by default. Without a reset, the first grand final decides the title.
  - The loser of a walkover still drops into the losers' bracket.
- **Round robin**
  - Fixtures use the circle method; when a group has an odd number of entries, each entry sits out one round.
  - Entries are allocated to groups in a snake by seed.
  - Standings rank by wins. A tie between two entries goes to head-to-head. Otherwise ties go to frame difference, then frames won, then draw order.
  - A walkover counts as a win with no frames.
- **Results**
  - The winner must reach `FramesToWin` exactly, and there are no draws.
  - `Handicap` is stored on `Entry` but does not yet change race lengths.
  - `Frame` records are a separate log and are not reconciled with the score until M4.
  - Completing a match and awarding a walkover are `internal` to the domain. They go through `Bracket`/`Tournament` so that progression always runs.
- **Scope**
  - The `GroupsThenKnockout` and `Swiss` formats throw "not supported yet".
  - League, Ranking, Rating and Challenge are minimal entities. Their behaviour comes in M6, M7 and M10.

### M2

- **Packages**
  - `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.3 is in Infrastructure.
  - `Microsoft.EntityFrameworkCore.Design` 10.0.13 is in Web. `dotnet ef` needs it in the startup project.
  - `dotnet-ef` 10.0.13 is a local tool listed in `dotnet-tools.json`.
- **Domain changes for EF**
  - Entities have private parameterless constructors so that EF can load them.
  - `Ranking` keeps a list of `RankingEntry` instead of a dictionary.
  - Membership and roster id sets use `List<Guid>`, and duplicates are prevented, because EF cannot map `HashSet<Guid>`.
- **Mapping**
  - EF's default naming is used (PascalCase, so identifiers need quoting in hand-written SQL), and table names are plural.
  - Enums are stored as text, up to 32 characters.
  - Domain-generated ids are configured `ValueGeneratedNever`.
  - `MatchLength`, `FormatOptions`, `MatchSlot` and the optional `SlotLink` are EF complex properties, stored as columns on the owning table.
  - `Frame`, `Rubber` and `RankingEntry` are owned tables with composite keys.
  - Player organisation memberships and team rosters are `uuid[]` columns with GIN indexes, not join tables. Revisit in M8 if permissions need more.
  - The entry ids in match slots and results are indexed, but they are not foreign keys.
- **Delete rules**
  - Children inside an aggregate cascade.
  - References across aggregates restrict (organisation, player, team).
  - Optional references are set to null (table, home venue).
  - Deleting a player cascades to their ratings and ranking entries.
- **Development database**
  - `docker-compose.yml` runs Postgres 16 with the local-only credentials `rackline` / `rackline_dev`. The same credentials are in `appsettings.Development.json`.
  - With `Database:MigrateAndSeedOnStartup`, which is set only in Development, the app migrates and then seeds.
  - `DevelopmentSeeder` contains only invented data and does nothing if any organisation already exists.
- **Integration tests**
  - They live in `Rackline.Web.Tests/Persistence`, with no separate Infrastructure test project.
  - Each test class gets a throwaway database that is dropped afterwards.
  - `[PostgresFact]` skips when `RACKLINE_TEST_CONNECTION` is not set, and the tests also carry `Category=Integration`.
  - In the cloud sandbox Docker has no running daemon, so the tests were run against Postgres 16 installed with apt.

### M3

- **Packages**
  - `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13 is in Infrastructure.
  - `Microsoft.AspNetCore.Mvc.Testing` 10.0.13 is in Web.Tests, for `WebApplicationFactory`.
- **Authentication**
  - It uses ASP.NET Core Identity's built-in API endpoints under `/api/auth`: register, login, refresh and so on.
  - Clients use a bearer token, or a cookie with `?useCookies=true`. Tokens last one hour.
  - Unauthorised `/api` calls get 401/403, never a redirect.
  - Passwords must be at least 10 characters, and emails must be unique.
  - `ApplicationUser` is not linked to `Player` yet.
- **Roles**
  - Admin, Organiser, Referee and Player have fixed ids and are created by the migration (`HasData`) in every environment.
  - Self-registered users have no role.
  - An Admin replaces a user's roles with `PUT /api/users/{id}/roles`. The change takes effect at the user's next sign-in, because roles are carried in the token.
  - The first Admin comes from `Seed:AdminEmail` and `Seed:AdminPassword` (user-secrets or environment variables) when the app migrates on startup.
- **Authorisation**
  - Every `GET` is public.
  - `ManageTournaments` (Admin or Organiser) covers tournament create, update and delete, entries, seeding, the draw and status changes.
  - `ScoreMatches` (Admin, Organiser or Referee) covers starting a match, results and walkovers.
  - `ManageUsers` (Admin only) covers listing users and setting roles.
  - Any Organiser can manage any tournament until per-organisation permissions arrive in M8.
- **Endpoints** (all under `/api`)
  - `tournaments`: `GET` (list, with `?status=`), `GET {id}`, `POST`, `PUT {id}` and `DELETE {id}`.
    - Status changes: `POST {id}/open-registration`, `/start`, `/complete` and `/cancel`.
  - Entries: `POST {id}/entries`, `PUT {id}/entries/{entryId}` (seed, handicap) and `DELETE {id}/entries/{entryId}`.
  - Draw: `POST {id}/draw?randomSeed=` and `DELETE {id}/draw`.
  - Matches: `POST {id}/matches/{number}/start?tableId=`, `/result` and `/walkover`.
  - Also: `GET players?search=`, `GET users/me`, `GET users?email=` and `PUT users/{id}/roles`.
  - Commands that change a tournament return its full detail. Create returns 201 with a `Location` header, and delete returns 204.
- **The draw**
  - The response includes the `randomSeed` used. Passing that seed again reproduces the draw exactly.
  - The seed is not stored.
- **Validation and errors**
  - Requests are validated at the boundary with .NET 10's built-in minimal-API validation (DataAnnotations plus `IValidatableObject`), which returns 400 with a validation problem. Enums travel as strings.
  - Errors use RFC 9457 problem JSON:
    - unknown resources are 404;
    - a broken domain rule (`DomainException`) is 409 "Not allowed";
    - a concurrent edit or a clashing unique index is 409 "Conflicting change".
  - For `/api`, the HTML "not found" page is switched off.
- **Concurrency**
  - `Tournament` and `Match` use Postgres `xmin` as a row version. This needs no schema change.
  - Two results that feed the same next match cannot both save. The second gets 409 and can be retried.
  - A filtered unique index on (`TournamentId`, `Seed`) stops two concurrent requests giving out the same seed.
- **Layering**
  - Commands live in four small Application classes: `TournamentLifecycle`, `TournamentEntries`, `TournamentDraws` and `MatchResults`.
  - They work through `ITournamentRepository` and `IReferenceData`.
  - Reads go through `ITournamentQueries` and `IPlayerQueries`, which use `AsNoTracking` and project with `Select`.
  - Application services are registered in Web, the composition root, so the Application project needs no DI package.
  - Time comes from `TimeProvider`, which tests replace with a fixed clock.
- **Tests**
  - Application tests use in-memory fakes.
  - API tests run the real app in a `Testing` environment against a throwaway Postgres database, and sign in through `/api/auth/login`.

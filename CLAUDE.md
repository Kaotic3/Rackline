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
  - `Frame` records are a separate log and are not reconciled with the score until M4. (Superseded in M4: frames now build the score.)
  - Completing a match and awarding a walkover are `internal` to the domain. They go through `Bracket`/`Tournament` so that progression always runs.
- **Scope**
  - The `GroupsThenKnockout` and `Swiss` formats throw "not supported yet".
  - League, Ranking, Rating and Challenge are minimal entities. Their behaviour comes in M6, M7 and M10. (Leagues done in M6; rankings and ratings in M7.)

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
  - Player organisation memberships and team rosters are `uuid[]` columns with GIN indexes, not join tables. (M8 kept them: staff permissions use a separate `Appointments` table.)
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
  - Any Organiser can manage any tournament until per-organisation permissions arrive in M8. (Superseded in M8: organisers and referees are appointed per organisation.)
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

### M4

- **Scoring by frame**
  - During a live match the score is the count of frames won.
  - `Tournament.RecordFrame` adds a frame. The frame that reaches the target completes the match and moves the players on.
  - `UndoLastFrame` takes a frame back while the match is live. A completed result cannot be corrected yet; that is future work.
  - The old direct live-score setter has been removed.
  - Once frames have been recorded, a whole result (`RecordResult`) is refused, so the score and the frames always agree.
- **Notifications**
  - `ILiveNotifier` (Application) is called only after a successful save.
  - `MatchChanges` snapshots a tournament's matches before a command. Only the matches whose `MatchDto` actually changed are broadcast (for example, the finished match and the next match the winner moves into).
  - Status, draw and entry changes send `TournamentUpdated`.
  - A failed broadcast is logged and never fails the command.
- **SignalR**
  - The hub, `LiveHub`, is at `/hubs/live`. Clients can only listen: `WatchTournament`, `UnwatchTournament`, `WatchMatch` and `UnwatchMatch` join and leave the `tournament:{id}` and `match:{id}` groups.
  - Scoring always goes through the authorised API or the referee pages, so the hub needs no authorisation.
  - Events:
    - `MatchUpdated` (a `MatchDto`) goes to the match group.
    - `MatchesUpdated` (tournament id plus the changed matches) goes to the tournament group.
    - `TournamentUpdated` (tournament id plus status) goes to the tournament group.
  - Enums travel as strings, as in the API.
- **Notifier placement**
  - `SignalRLiveNotifier<THub>` lives in Infrastructure, as the solution layout says. It needs `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, which is part of .NET, not a NuGet package. The concrete hub is supplied by Web.
  - The notifier also publishes to the in-process `LiveFeed`, which interactive Blazor pages subscribe to. They need no SignalR client of their own.
- **API**
  - `POST /api/tournaments/{id}/matches/{number}/frames` takes `{ winnerSlot, breakAndRun, points1, points2 }`.
  - `DELETE …/frames/last` takes back the last frame.
  - Both are under the `ScoreMatches` policy.
  - `MatchDto` now includes `BreakAndRuns1` and `BreakAndRuns2`.
- **Referee pages**
  - `/referee` lists the tournaments in progress. It uses static server-side rendering (SSR).
  - `/referee/tournaments/{id}` lists the live and ready matches, and `/referee/tournaments/{id}/matches/{number}` is the scoring screen.
  - Both of those use Interactive Server, refresh from `LiveFeed`, and take a fresh DI scope (and so a fresh DbContext) for each action, so the circuit never holds stale tracked data.
  - The scoring screen:
    - starts a match on a chosen table, or awards a walkover;
    - has large "+ Frame" buttons and a break-and-run toggle for the next frame;
    - asks for confirmation before the deciding frame;
    - can undo the last frame.
- **Page sign-in**
  - `/account/login` is a static SSR form using `SignInManager`, which issues a cookie. It only redirects to local URLs.
  - Signing out is a form post to `/account/logout`, protected by antiforgery.
  - There is a simple `/account/access-denied` page.
- **Authentication challenges**
  - Identity's API scheme sends every challenge to the bearer handler, so pages got a bare 401. Challenges and forbids now go through the cookie handler: pages redirect to sign-in, and the M3 cookie events still return plain 401/403 for `/api`.
  - The only side-effect is that bearer-token callers no longer get a `WWW-Authenticate: Bearer` header.
- **Packages**
  - `Microsoft.AspNetCore.SignalR.Client` 10.0.13 is in Web.Tests only, to test the hub end to end.
- **Tests**
  - SignalR is tested with the real client over long polling against the test server.
  - The pages are tested over HTTP (cookie sign-in through the real form, then the server-rendered output).
  - The interactive behaviour was checked by hand in headless Chromium with a throwaway Playwright script (not in the repo): sign-in, frames, break and run, undo, the deciding-frame confirmation, completion, and starting a match on a table.

### M5

- **Pages**
  - All public pages are static SSR, with no Blazor circuit. A test checks that they have none.
  - `/` shows what is on now.
  - `/tournaments` lists tournaments under "In progress", "Coming up" (taking entries or drawn) and "Finished" (completed or cancelled).
  - `/tournaments/{id}` shows the draw.
    - Knockouts show one column per round, named "Final", "Semi-final", "Quarter-final", then "Round n".
    - Double elimination shows the winners' bracket, the losers' bracket and the grand final.
    - Round robins show each group's table and its fixtures.
  - `/matches/{id}` is a live scoreboard.
  - `/players` (with `?search=`) is the player list, and `/players/{id}` is a profile.
  - `/head-to-head?a=&b=` compares two players.
  - Unknown ids return 404 through `NavigationManager.NotFound()`.
  - Draft tournaments are not public: they are left out of lists and return 404.
- **Bracket display**
  - Void matches (bye against bye, and an unused grand-final reset) are hidden. Bye matches are shown faded.
  - Brackets scroll sideways, and later rounds are spaced so each match sits between the two that feed it. There are no connector lines.
- **Live updates on public pages**
  - They use the SignalR JavaScript client (`@microsoft/signalr` 10.0.0, MIT). It is vendored, unmodified, in `wwwroot/lib/signalr` with its licence, so no CDN or third-party script is involved.
  - `wwwroot/js/live.js` is loaded on every page but connects only when a page carries `data-live-tournament` or `data-live-match`.
  - Score changes update the page in place, with a short highlight.
  - Anything structural reloads the page: a state change, a slot change or `TournamentUpdated`. Brackets and tables therefore always come from the server.
  - The script re-syncs after Blazor's enhanced navigation (`enhancedload`). After a reconnect it re-joins its group and reloads, to catch missed updates.
- **Standings**
  - `GroupStandings` now works from a plain `GroupResult` record. The overload taking domain matches is kept.
  - `GroupTables` in Application builds the public tables from DTOs using the same domain rules.
  - The draw order is not stored, so the final tie-break on public tables is seed, then entry order.
- **Player records**
  - `GetPlayedMatchesAsync` returns a player's finished matches against a real opponent (no byes), from that player's side. It uses three queries in total, avoiding N+1.
  - `PlayerRecord` gives played, won, lost, walkovers, frames and break-and-runs. Walkovers count as wins and losses but add no frames, matching the round-robin standings.
  - `HeadToHead` filters those matches by opponent.
- **Display**
  - Dates are shown as UTC calendar dates in en-GB format (for example, 17 Jan 2026).
  - Disciplines, formats and statuses have British English labels (`Labels`).
- **Seed data**
  - The seeder adds "Winter Nine-Ball Classic", a finished, invented 8-player knockout scored by frame with one upset, so profiles and head-to-heads have history.
  - To see it locally, drop the local `rackline` database so it re-seeds.
- **Tests**
  - Application tests check `GroupTables` against the domain standings, plus `PlayerRecord` and `HeadToHead`.
  - Public pages are tested over HTTP against tournaments created through the API.
  - The live behaviour was checked by hand in headless Chromium. One frame updated the tournament and match pages in place without a reload. The deciding frame reloaded both, showing the result and the winner moved on.

### M6

- **League model**
  - `League` is the aggregate root. It holds seasons, which hold teams and fixtures, and fixtures hold rubbers.
  - `ILeagueRepository` loads a whole league for any change. That is simple, and fine at club-league sizes.
- **Season rules (`SeasonRules`)**
  - They cover rubbers per fixture (1–30), the rubber length (race-to or best-of), and points for a win, a draw and each rubber won.
  - A win must be worth at least a draw.
  - Rules can be changed only while the season is being set up.
- **Season lifecycle**
  - Setup → Scheduled → Completed.
  - Teams can be added and removed only during set-up.
  - `GenerateFixtures` moves the season to Scheduled. `Complete` needs every fixture played.
- **Rosters**
  - A player may be registered with only one team per season.
  - Players can be added and removed until the season is completed.
  - A rubber's players must be on the right team's roster. A player may play more than one rubber in a fixture.
- **Fixture generation (`LeagueSchedule`)**
  - It is pure and deterministic.
  - Pairings use the circle method: one fixture per team per round, and odd team counts give a week off.
  - One round every `daysBetweenRounds` days (default 7) from the first date. Every fixture must fall within the season's dates.
  - Home and away is the default: the second half mirrors the first with venues swapped.
  - Venue rule:
    - Number the teams round a circle of odd size, adding an imaginary team when the count is even.
    - A team is at home against the teams up to halfway round the circle from it.
    - So in a single round robin every team is at home in half its fixtures, to within one. This is tested for every size from 2 to 20.
  - Manual one-off fixtures were removed; all fixtures come from generation.
- **Results**
  - A result is entered as all its rubbers at once, so a league secretary enters or corrects a whole team match.
  - Re-entering replaces the rubbers, which is how corrections are made. Results can be entered only while the season is Scheduled.
  - Each rubber must be complete under the rubber length: exactly one player reaches the target. A fixture can be drawn when the rubber count is even.
  - `Reschedule(fixture, date)` moves a fixture. A null date postpones it ("date to be confirmed"). A fixture that has been played cannot be rescheduled.
- **League table (`LeagueTable`)**
  - Points come from the rules.
  - Ties go to rubber difference, then rubbers won, then team name. Teams level on all three share a position.
  - Like group standings, it works from plain `FixtureOutcome` records, so the Application `SeasonTable` builds the public table from DTOs with the same rules.
- **Migration**
  - `LeagueSeasonsAndResults` adds the rules, the statuses and fixture rounds.
  - Existing rows get sensible defaults: Setup, 1 rubber per fixture as a race to 1, 2 points for a win and 1 for a draw.
  - A data fix-up marks seasons that have fixtures as Scheduled, and fixtures that have rubbers as Completed.
  - It was checked by migrating a database seeded with the M5 code. Dropping and re-seeding the local database is still the cleanest way to get the new sample league.
- **Concurrency**
  - `Fixture` uses Postgres `xmin` as a row version, so two people entering the same result cannot overwrite each other unseen.
- **API** (under `/api`)
  - The `ManageLeagues` policy (Admin or Organiser) covers changes. Reading is public.
  - `leagues`: `GET`, `GET {id}`, `POST`, and `POST {id}/seasons`.
  - `seasons/{id}`: `GET` (season and table), `POST teams`, `DELETE teams/{teamId}`, and `PUT`/`DELETE teams/{teamId}/players/{playerId}`.
  - Also under `seasons/{id}`: `POST fixtures` (generate), `PUT fixtures/{fixtureId}/result`, `POST fixtures/{fixtureId}/reschedule` and `POST complete`.
  - Season commands return `{ season, table }`.
  - Incoming dates and times are converted to UTC at the boundary.
- **Pages**
  - Public pages (static SSR):
    - `/leagues` and `/leagues/{id}`;
    - `/seasons/{id}`, showing the rules, the table, fixtures by week and team rosters;
    - `/seasons/{id}/fixtures/{fixtureId}`, showing the rubbers.
  - The organiser result-entry screen is `/organiser/seasons/{id}/fixtures/{fixtureId}` (Interactive Server, `ManageLeagues`). It has:
    - player pickers from each team's roster and scores for each rubber, with a running total;
    - a "Save" button that also serves for corrections;
    - controls to move the fixture or postpone it.
  - "Enter result" links appear on the public pages only for organisers.
  - League and season set-up (creating leagues, seasons and teams, rosters, generating fixtures) is API-only for now. Result entry is the frequent task, so it got the screen.
- **Bug fixes found along the way**
  - **Production API errors came back as HTML.** Outside Development, ASP.NET Core rewrites the request path to the error page before calling `IExceptionHandler`, so the "is this /api?" check failed and API 500s were returned as HTML.
    - The handler now reads the original path from `IExceptionHandlerPathFeature`, and writes problem JSON itself if the shared writer declines.
    - A regression test forces an API crash and checks for problem JSON with no internal details. The test was shown to fail without the fix.
  - **The site header ran off the edge on phones.** It now wraps on narrow screens.

### M7

- **Ratings: Elo, not Glicko-2**
  - Elo is the simpler of the two named options.
  - Everyone starts at 1500. A match moves each player by K × (result − expected result), where the expected result is 1 / (1 + 10^((opponent − player) / 400)).
  - Only win or lose counts; the frame margin does not.
  - K is 32 for a player's first 20 rated matches (shown as "provisional") and 16 after that. Each player's own K is used, so the change is not always zero-sum.
  - Ratings are per player per discipline.
- **What is rated**
  - Tournament matches completed by a result, between two entries. Walkovers and byes are not rated, because no frames were played.
  - Rating happens in the same save as the result: `RecordResult`, or the deciding frame in `LiveScoring`. If the save fails, nothing is rated.
  - League rubbers are not rated yet: a corrected fixture result replaces its rubbers, which would need ratings to be undone. Challenges come in M10.
  - Matches in a tournament that is later cancelled still count, as they were played.
- **Rating history**
  - Each rated match stores a `RatingChange` for each player (before, after, opponent, match, time).
  - A unique index on (player, match) stops a match being rated twice. `Rating` uses Postgres `xmin`, so two results for the same player cannot overwrite each other unseen (the second gets 409).
  - `MatchId` is not a foreign key, so the history survives if a cancelled tournament is deleted.
- **Rebuild**
  - `Elo.Replay` rates a whole history from scratch, oldest first. Tests check it gives the same answer as rating results one at a time.
  - `POST /api/ratings/rebuild` (Admin only, `RebuildRatings`) replaces every rating and change from the stored results, in one transaction. Use it after a formula change or to tidy up older data.
  - The seeder builds its ratings with the same replay, so they match its results exactly.
- **Finishing positions (`FinishingPositions`)**
  - Knockouts: the champion is first. Everyone else is placed by the stage at which they went out, and players out at the same stage share a position (so 1, 2, 3, 3, 5, 5, 5, 5).
  - Double elimination places players by the losers' bracket round or grand final of their second loss.
  - Round robin places players by their group position. With several groups, every group winner shares first place, and so on.
  - The final tie-break in group tables (domain and public pages) is now seed, then entry id, as the draw order is not stored.
- **Rankings**
  - A ranking has a name, an organisation, a discipline, a season, points for 1st, 2nd and so on (up to 64 places), and points for everyone below that. Points may never rise for a lower place.
  - A tournament counts towards at most one ranking, set by `rankingId` on create or update, before the draw. It must be the same organisation and discipline. A national ranking taking regional events waits for the hierarchy in M8. (Done in M8.)
  - Points are awarded when the tournament is completed, in the same save. Cancelled tournaments award nothing.
  - Awards are stored per tournament and player (`RankingAwards`, keyed by ranking, tournament and player), so a tournament can count only once, and the table can be explained.
  - The table totals the points. Players on equal points share a position.
  - The points scheme cannot be changed after creation yet.
- **Migration (`RankingsAndRatings`)**
  - Replaces `RankingEntries` with `RankingAwards`, adds the ranking name and points scheme, `Tournament.RankingId` and `RatingChanges`.
  - Old ranking totals had no tournaments behind them, so they are dropped. Existing rankings get a name and a default points scheme. Existing ratings are kept; a rebuild replaces them.
  - It was checked by migrating a database seeded with the M6 code. Dropping and re-seeding the local database is still the cleanest way to get the new sample data.
- **API** (under `/api`)
  - `rankings`: `GET`, `GET {id}` (table, points scheme and tournaments) and `POST` (`ManageRankings`: Admin or Organiser).
  - `ratings?discipline=`: the leaderboard. `POST ratings/rebuild`: Admin only.
  - `players/{id}/ratings` (ratings and the last 50 changes) and `players/{id}/rankings`.
  - Tournament details now include `rankingId` and `rankingName`.
- **Pages** (static SSR)
  - `/rankings` and `/rankings/{id}`: table, points scheme, and the tournaments that count.
  - `/ratings`: a leaderboard per discipline, with provisional ratings marked. Players on exactly the same rating share a position.
  - Player profiles show ratings, ranking positions and the last 10 rating changes. Tournament pages link to their ranking.
  - "Rankings" and "Ratings" are in the site header.
- **Seed data**
  - "Northland 9-ball rankings" (2026). The Winter Nine-Ball Classic has awarded its points, and the Eastbridge Spring Open counts towards it.

### M8

- **Appointments replace the site-wide Organiser and Referee roles**
  - A user is appointed Organiser or Referee at an organisation (`Appointment`: organisation, user, role; one per user per organisation). An appointment covers that organisation and everything below it.
  - An organiser can do everything a referee can.
  - Admin stays a site-wide role and may do anything. Player is kept, but grants nothing yet.
  - The Identity roles Organiser and Referee are retired, so `PUT /api/users/{id}/roles` now accepts only Admin and Player. Keeping them as well would have meant two sources of truth that could drift apart.
  - Appointments are read from the database on every check, not carried in the token. A new or removed appointment counts at once, without signing in again.
- **Where permissions are checked**
  - Every command in the Application layer checks the organisation it touches through `OrganisationAccess`, and throws `ForbiddenException` (403 "Forbidden") otherwise. Unknown resources are still 404, checked first.
  - Running tournaments, leagues and rankings needs Organiser. Starting matches, results, frames and walkovers need Referee. The rating rebuild and user roles stay Admin-only.
  - The endpoint policies (`ManageTournaments`, `ScoreMatches`, `ManageLeagues`, `ManageRankings` and the new `ManageOrganisations`) now only ask "an admin, or appointed with at least this role somewhere". They decide who reaches staff endpoints and pages; the command decides where.
  - `ICurrentUser` comes from the request. Interactive pages have no request when they run a command, so they hand their signed-in user to a fresh scope (`ScopedWork.RunAsAsync`).
  - The policy handler uses its own short-lived scope. On a server-rendered page the layout's authorisation check can run alongside the page's queries, and a DbContext allows one operation at a time.
- **Hierarchy rules**
  - Only an admin can add a top-level organisation. An organiser can add organisations below their own. The existing rule that a parent sits above its child (international → national → regional → club) means the hierarchy cannot loop.
  - An organiser can appoint, change and remove organisers and referees at their organisation or below it. They can share their rights but never gain more.
  - Appointments are made by email address, so the person needs an account first. An unknown email is 404, which tells an organiser whether an address has an account; that seemed acceptable for staff.
  - Staff lists include email addresses, so only that organisation's organisers (or an admin) may read them.
- **Rankings across the hierarchy** (deferred from M7)
  - A tournament may count towards a ranking of its own organisation or any organisation above it (`OrganisationLineage`), so a club event can count in a national ranking.
  - Choosing a ranking needs organiser rights over the ranking's organisation as well, so a club cannot put its own events into a national ranking. Keeping the current ranking needs no extra rights, so the club can still edit the event.
- **New API** (under `/api`; reading is public)
  - `organisations`: `GET`, `GET {id}` (parent, children, venues and tables, public tournaments, leagues, rankings, member count), `POST` and `PUT {id}` (rename).
  - `organisations/{id}/venues` (`POST`) and `organisations/venues/{venueId}/tables` (`POST`).
  - `organisations/{id}/staff`: `GET`, `PUT` (`{ email, role }`, appoints or changes the role) and `DELETE {userId}`.
  - `organisations/{id}/players/{playerId}`: `PUT` and `DELETE` (membership). `POST players` adds a player as a member of an organisation.
  - `GET users/me/appointments`.
- **Migration (`OrganisationStaff`)**
  - Adds `Appointments` (unique on organisation and user; cascades from the organisation and the user).
  - Holders of the old Organiser and Referee roles are appointed at every top-level organisation, which keeps everything they could do. Anyone who held both becomes an organiser. The two roles are then deleted.
  - It was checked by migrating a database made with the M7 code, with one user holding both roles and one holding Referee only.
- **Pages**
  - Public (static SSR): `/organisations`, the hierarchy as a tree, and `/organisations/{id}`. Tournament pages link to the organisation that runs them.
  - `/organiser` lists the signed-in user's appointments.
  - `/organiser/organisations/{id}/staff` (Interactive Server) lists staff, appoints by email with a role, changes roles and removes staff.
  - `/referee` lists only the tournaments the user may score, and a scoring page says so up front when they cannot.
  - "Enter result" links show only for organisers of the league's organisation, not for every organiser.
  - The header has "Organisations" for everyone, and "Your organisations" for staff.
- **Seed data**
  - The Eastbridge Spring Open now belongs to the Corner Pocket Club and still counts towards the national 9-ball ranking.
  - No staff accounts are seeded, as they would need passwords in the repository. The admin from user-secrets can appoint people on the staff page.
- **Tests**
  - The test factory adds a test federation above every club the tests create. Staff signed in through `ClientForAsync(StaffRole.…)` are appointed there by default, so earlier tests keep their meaning. The hierarchy tests make their own organisations.
- **Bug fixes found along the way**
  - **Player profiles failed about one request in ten under load** (from M7). A static page renders once at its first slow `await`, and the profile set the player before loading its record, so that render could see half-loaded state. It now loads everything before setting any field. A regression test sends 300 requests, 20 at a time.
  - **Organisation levels sorted alphabetically.** Enums are stored as text, so ordering by level in SQL put "Club" before "International". Those lists are now sorted in memory.

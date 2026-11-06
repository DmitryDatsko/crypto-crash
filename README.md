# Crypto Crash

A real-time cryptocurrency prediction game backend built with ASP.NET Core. Players choose whether the BTC/USDT price will rise, fall, or remain unchanged during a short round. Live price movements drive a race-style visualization, while the server manages rounds, records bets, and submits winner balance updates to a Monad smart contract.

This repository contains the backend API and game server. The frontend and smart contract source are maintained outside this repository and are required for the complete application.

## Features

- Live BTC/USDT prices from Bybit's public spot WebSocket feed, with reconnect backoff.
- Automatically scheduled betting windows, game rounds, and result broadcasts.
- Real-time game state, race positions, timers, and bet pool updates through SignalR.
- Ethereum-compatible wallet signature authentication with JWT cookies.
- PostgreSQL storage for games and bets, with Entity Framework Core migrations.
- Monad RPC integration for transaction receipt checks and batched winner balance updates.
- HTTP access to recent game history.

## Technology

| Area | Implementation |
| --- | --- |
| Application | C# / .NET 9 / ASP.NET Core |
| Real-time communication | SignalR |
| Persistence | PostgreSQL, Entity Framework Core, Npgsql |
| Blockchain integration | Nethereum, Monad-compatible RPC |
| Authentication | Wallet signatures, JWT bearer authentication, HTTP-only cookies |
| Market data | Bybit WebSocket API (`tickers.BTCUSDT`) |
| Deployment | Multi-stage Docker build, Docker secrets, Swarm-oriented stack configuration |

## How it works

1. `CryptoPriceService` subscribes to Bybit's BTC/USDT ticker stream.
2. `BettingService` creates a game in PostgreSQL and opens a betting window, lasting 12 seconds by default.
3. An authenticated client submits a bet through `GameHub.PlaceBet`, including the game ID, amount, side, and transaction hash. The backend checks the transaction sender and destination and waits for block confirmations before recording the bet.
4. A 15-second round follows. Price changes move the long and short race positions, which are broadcast to connected clients.
5. The first and last prices observed during the round determine the result: `Long`, `Short`, or `Tie`. Fewer than two price samples also produce a tie.
6. The server saves the result, broadcasts it, calculates payouts, and calls the contract's `addWinnersBalance` function in batches of up to 200 entries.
7. After a five-second pause, the next betting window opens.

Winners receive their stake plus a proportional share of the losing pool after the configured margin:

```text
payout = stake + (stake / total winning stakes) × losing pool × (1 − margin)
```

The default margin is 5% of the losing pool. If there are no winning bets, the backend does not submit a winner balance update. The race positions are a visual representation; the result is determined by the sampled prices, not by the animation.

### Project structure

```text
Configuration/       Runtime settings and secret-name mapping
Controllers/         Authentication and game history HTTP endpoints
Data/                Entity Framework Core database context
DbInitializer/       Automatic migration application at startup
Filters/             Authentication checks for SignalR method calls
Hubs/                Client connections, bet submission, and event names
Migrations/          PostgreSQL schema migrations
Models/              Database entities, DTOs, and contract function bindings
Services/Hub/        Price feed, game loop, and in-memory bet pool
Services/Monad/      RPC transaction checks and contract balance updates
Services/Token/      Wallet identity extraction from JWT cookies
Program.cs           Dependency injection, authentication, CORS, and routing
```

The active round and bet pool live in memory; games and bets are persisted in PostgreSQL. The game loop currently assumes a single backend instance.

## Local development

### Prerequisites

- .NET 9 SDK.
- A running PostgreSQL instance. The deployment configuration uses PostgreSQL 18.
- Access to Bybit's public WebSocket endpoint and a Monad-compatible RPC endpoint.
- A deployed compatible smart contract and its authorized owner's private key, funded for transaction fees. The contract must expose `addWinnersBalance((address,uint256)[])` for settlement.

Use a dedicated development wallet and test network. There is no bundled contract deployment or offline blockchain mock.

### 1. Clone the repository

```sh
git clone https://github.com/DmitryDatsko/crypto-crash.git
cd crypto-crash
```

### 2. Configure the application

Create a database named `crypto_crush`, then create a `.env` file in the project root:

```dotenv
EnvVariables__PostgresConnectionString="Host=localhost;Port=5432;Database=crypto_crush;Username=postgres;Password=YOUR_DB_PASSWORD"
EnvVariables__RpcUrl=https://YOUR_RPC_ENDPOINT
EnvVariables__NetworkId=YOUR_NETWORK_ID
EnvVariables__ContractAddress=YOUR_DEPLOYED_CONTRACT_ADDRESS
EnvVariables__ContractOwnerPrivateKey=YOUR_DEVELOPMENT_PRIVATE_KEY
EnvVariables__JwtTokenSecret=YOUR_RANDOM_SECRET
```

Replace every placeholder. `NetworkId` must match the RPC endpoint's `net_version` response. Use a cryptographically random JWT secret of at least 64 ASCII characters; for example, generate one with `openssl rand -hex 64`.

The double underscores map environment variables to the `EnvVariables` configuration section. The existing `.env.example` lists uppercase snake-case names, but those names are not mapped to this section by the current startup code; use the names above instead.

`.env` is excluded from Git and the Docker build context. Never commit private keys or real credentials.

Game settings are defined in `appsettings.json` and can also be supplied through the same `EnvVariables__` prefix:

| Setting | Default | Purpose |
| --- | --- | --- |
| `BetTime` | `12` | Betting window in seconds |
| `GameDuration` | `15` | Round duration in seconds |
| `DelayAfterGame` | `5` | Pause between rounds in seconds |
| `Margin` | `0.05` | Fraction deducted from the losing pool |
| `TotalPixels` | `270` | Race visualization distance parameter |
| `MempoolTimeSec` | `5` | Timeout for discovering a submitted transaction |
| `RequiredConfirmations` | `12` | Block-depth threshold used during transaction verification |
| `CookieName` | `VerifyAddress` | Authentication cookie name |

### 3. Run the backend

```sh
dotnet restore
dotnet dev-certs https --trust
dotnet run --launch-profile https
```

The HTTPS endpoint is `https://localhost:7015`; the HTTP profile also binds `http://localhost:5161`. Use HTTPS for browser authentication because the cookies are always marked `Secure`.

Database migrations run automatically at startup, so the database must already be reachable and the configured user must have schema modification permissions. The price feed and game loop start with the application.

Check the history endpoint:

```sh
curl https://localhost:7015/api/game/get-history?pageSize=5
```

Development CORS allows `http://localhost:3000`. Browser clients must include credentials for cookie-based authentication. Other frontend origins require updating the CORS policy in `Program.cs`.

## API

### HTTP endpoints

| Method | Route | Description |
| --- | --- | --- |
| `GET` | `/api/auth/nonce` | Generate a random message nonce |
| `POST` | `/api/auth/verify` | Recover a wallet address from a signed message and issue an authentication cookie |
| `GET` | `/api/auth/me` | Return the authenticated wallet address; authentication required |
| `POST` | `/api/auth/logout` | Delete the authentication cookie; authentication required; currently returns HTTP 401 |
| `GET` | `/api/game/get-history?pageSize=30` | Return recent game IDs and winning sides, newest first |

The verification endpoint accepts a JSON object with `message` and `signature` fields. JWTs expire after one day. Although Swagger services are registered, Swagger middleware and a UI endpoint are not enabled.

### SignalR

Connect to `/gamehub`. Clients receive a state snapshot on connection; invoking hub methods requires authentication.

The bet submission method is:

```text
PlaceBet(gameId, amount, side, txHash)
```

`side` accepts `Long`, `Short`, or `Tie`. The client submits the blockchain transaction separately and supplies its hash to the backend.

| Server event | Contents |
| --- | --- |
| `OnConnected` | Current game ID, phase flags, bets, and total pool (`bank`) |
| `BettingState` | Game ID and betting/game phase flags |
| `Timer` | Elapsed seconds in the current phase, as a two-decimal string |
| `RaceTick` | Long and short race positions |
| `BetAccepted` | Confirmation that the bet was recorded |
| `BetRejected` | Rejection reason |
| `Bets` | Current bets and total pool |
| `GameResult` | Winning side, phase flags, and bet results |

## Deployment

Build the backend image with:

```sh
docker build -t crypto-crush:latest .
```

`compose.yaml` describes a Swarm-oriented deployment, not a self-contained `docker compose up` setup. It uses an overlay network and expects:

- Prebuilt `crypto-crush:latest` and `crypto_crush_front:latest` images available to the deployment nodes.
- An existing external network named `proxy`.
- External Docker secrets: `db_password`, `postgres_connection_string`, `contract_address`, `contract_owner_private_key`, `jwt_token_secret`, `network_id`, and `rpc_url`.
- HTTPS termination at a reverse proxy. The backend container is configured to listen on HTTP port 8080; publishing port 8081 does not configure TLS.

The database connection string should use `crypto_crush_db` as its host inside the stack. The production CORS origin is currently fixed to `https://cryptocrash.dmytrodatsko.dev` in `Program.cs`.

## Current limitations

This is not an audited real-money wagering service. Before using it beyond development, the following areas need additional work:

- **Authentication replay protection:** generated nonces are not stored or checked when verifying signatures.
- **Bet validation:** transaction checks do not currently validate receipt success, transaction value or contract event contents, or transaction-hash reuse. Bet amounts are supplied by the client, and the betting-open flag is not enforced during submission.
- **Recovery and scaling:** active round state is held in one process, without restart recovery or coordination between replicas. Settlement does not have a persisted retry workflow.
- **Tests:** no automated test project is included in this repository.

Use test funds only until these paths have been hardened and tested.

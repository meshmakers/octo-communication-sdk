---
description: How an adapter acquires and renews its own access token for the adapter hub connection.
applies_to: src/Sdk.Adapters/AdapterOptions.cs, src/Sdk.Adapters/AdapterAccessToken*.cs, src/Sdk.Adapters/ConfigureAdapterAuthenticatorOptions.cs, src/charts/**
---

# Adapter hub authentication

The adapter acquires its **own** client-credentials access token at startup and presents it on the
`/{tenantId}/adapterHub` SignalR connection. Four pieces, in the order the token travels:

1. **`AdapterOptions.IssuerUri` / `ClientId` / `ClientSecret`** (section `Adapter`, i.e. env
   `OCTO_ADAPTER__ISSUERURI`, `__CLIENTID`, `__CLIENTSECRET`). `IsEnabled` is `IssuerUri && ClientId`
   — the secret is deliberately not part of the check, so a confidential client with a missing secret
   fails loudly at the token endpoint instead of silently degrading to an anonymous connection that
   looks healthy until the gate is armed. The tenant is `AdapterOptions.DedicatedTenantId` — or, on a
   pool member, the **lending** tenant from `AdapterPool:PoolTenantId`; see `docs/adapter-pool-membership.md`.
2. **`ConfigureAdapterAuthenticatorOptions`** (`IConfigureOptions<AuthenticatorOptions>`) projects
   those four onto the SDK's `AuthenticatorOptions`, which `AuthenticatorClient` reads.
3. **`AdapterAccessTokenService`** (`BackgroundService`) requests the token
   (`ApiScopes.OctoApiFullAccess`, `DefaultScopes.None` → exactly `octo_api`, no `offline_access`)
   and writes it into the singleton `IServiceClientAccessToken`.
4. **`AdapterHubClient`** was already handed that same instance. The SDK reads it through
   `HttpConnectionOptions.AccessTokenProvider` on every connection attempt, so a refresh reaches the
   next (re)connect with no notification path.

**Why the token is acquired at startup.** The token holder has to be filled before the first connect:
the writers on the pipeline execution paths fill it too late, and an adapter that presents a token only on
some reconnects makes the adapter-hub gate's `LogOnly` inventory unreadable.

🔴 **`acr_values=tenant:{TenantId}` is not optional.** A token request without it is
issued for the **system** tenant, and the controller then refuses the adapter on its own tenant route
with a 403. That is what `ConfigureAdapterAuthenticatorOptions` exists to pin.

🔴 **Unconfigured is a supported state and must stay one.** An adapter deployed without these keys must
keep connecting as before. Without a client id the service logs one warning, never calls the
authenticator, and the access token stays null — which makes the SDK send no `Authorization` header
and no `access_token` query parameter at all. A hard requirement here would take every adapter that
has not been given the keys down on upgrade.

**Renewal.** A live SignalR connection is authorized once, at connect. The *re*connect is the
exposure, and adapters reconnect routinely (controller rollout, node drain, network blip, the SDK's
own retry loop, a wake from scale-to-zero). Hence the loop: replace the token `RefreshSkew` (5 min)
before its own `exp`, retry every `RetryInterval` (30 s) after a failure, and **keep the previous
token on failure** — it is no less usable than none. The first acquisition runs inside `StartAsync`,
before `base.StartAsync`; hosted services start sequentially and this one is registered **before**
every other hosted service in both `AdapterBuilder` and `WebAdapterBuilder`, so the first hub
connection already carries a token rather than racing it.

⚠️ **`Adapter:IssuerUri` is a second key next to `octo-mesh-adapter`'s `Adapter:AuthorityUrl`.** Both
bind the same configuration section and name the same identity service, but `AuthorityUrl`
(`MeshAdapterConfiguration`, in the adapter repo) is the **inbound** issuer secured
`FromHttpRequest@2` routes accept. That type is unknown to this SDK and adapters without it (Loxone,
Modbus, Zenon, the simulation plug) still need an issuer, so the outbound credential carries its own
key. They normally hold the same value.

**Mostly not in this repo:** delivering the credentials into the adapter. The secret lives as a
`ServiceAccountConfiguration` in the tenant DB; the route runs through the `ValueOverride`s the
controller sends to the operator at deploy time and through the adapter chart. This repo's C# only
makes the adapter *able* to authenticate — but `src/charts/octo-plug-simulation` is itself one of
those adapter charts and carries the delivery half.

🔴 **Every new or changed SDK-based adapter chart has to render the three keys, or that adapter stays
anonymous.** The three env vars are read by `AdapterOptions`, which every adapter shares, so the
controller projects the credentials for *all* of them — but Helm silently ignores values a chart does
not read. Copy the block byte-identical from an existing adapter chart (for example
`src/charts/octo-plug-simulation` in this repository); `authUri` needs no plumbing — the operator writes it into every workload's context values
(`WorkloadContextValuesBuilder`). The value paths the controller writes are
`serviceAccountClientId` and the secret-flagged `secrets.serviceAccountClientSecret`; they are
pinned on the controller side in `PoolService`.

**Chart publishing.** `test/*` branches publish adapter charts into the shared dev bucket as a SemVer
prerelease, so an unpinned `ChartVersion` keeps resolving the newest stable chart.

Tests: `tests/Sdk.Common.Tests/Adapters/AdapterAccessTokenServiceTests.cs` (token published into the
shared holder, scope shape, unconfigured never calls the authenticator in either direction, valid
token not re-acquired, refresh-window replacement, failure keeps the previous token, tokenless
response not published, `StartAsync` acquires before returning in both shapes, and neither secret nor
token in the **rendered** log output) and `ConfigureAdapterAuthenticatorOptionsTests.cs`.

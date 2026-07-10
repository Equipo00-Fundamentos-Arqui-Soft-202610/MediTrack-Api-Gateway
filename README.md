# MediTrack API Gateway

Single entry point for all MediTrack backend microservices, implemented with [Ocelot](https://ocelot.readthedocs.io/) on ASP.NET Core 8.

## Overview

The Gateway listens on `http://localhost:5000` and routes requests to the appropriate microservice based on the URL prefix. All CORS headers are handled at this layer — individual services do not need CORS configuration.

## Route Map

| Gateway prefix | Downstream service | Port |
|---|---|---|
| `/treatment/{path}` | Treatment Service | 5162 |
| `/followup/{path}` | Follow-up Service | 5267 |
| `/appointments/{path}` | Medical Appointment Service | 5186 |
| `/analysis/{path}` | Medical Analysis Service | 5188 |
| `/reminders/{path}` | Reminder Service | 5080 |

The prefix is stripped before forwarding, except for `/reminders` which keeps the prefix (the Reminder Service uses it internally).

**Example:**
```
GET http://localhost:5000/treatment/api/v1/medications?patientId=1
        ↓ forwarded as
GET http://localhost:5162/api/v1/medications?patientId=1
```

## Prerequisites

- .NET SDK 8+ (or 9+ with `rollForward: latestMajor` already configured in `global.json`)
- All microservices running on their respective ports

## Running locally

```bash
dotnet run --project MediTrackApiGateway
```

The gateway starts on `http://localhost:5000`.

## Configuration

Routes are defined in [`MediTrackApiGateway/ocelot.json`](MediTrackApiGateway/ocelot.json). To add a new service, add a new entry to the `Routes` array:

```json
{
  "Key": "my-service",
  "UpstreamPathTemplate": "/my-service/{everything}",
  "UpstreamHttpMethod": [ "Get", "Post", "Put", "Patch", "Delete" ],
  "DownstreamPathTemplate": "/{everything}",
  "DownstreamScheme": "http",
  "DownstreamHostAndPorts": [
    { "Host": "localhost", "Port": 5XXX }
  ]
}
```

## Local development secrets

`Jwt:Key` is intentionally left empty in `appsettings.json` — it's shared with
Identity Service, Reminder-Service, Treatment-service and FollowUp-Service, so
each developer sets it once on their own machine:

```bash
dotnet user-secrets set "Jwt:Key" "<ask the team for the shared dev key>" --project MediTrackApiGateway
```

In production, the same value is set as the `Jwt__Key` environment variable
on the deploy provider (Render, etc.) — never committed to a file in this repo.

## Seed data

[`MediTrackApiGateway/seed-data.http`](MediTrackApiGateway/seed-data.http) contains 25 HTTP requests (5 per service) for populating local databases. Execute them in order using the JetBrains HTTP client or the VS Code REST Client extension.

Required startup order before running seed requests:

1. RabbitMQ
2. MySQL
3. Treatment Service
4. Medical Appointment Service
5. Follow-up Service
6. Medical Analysis Service
7. Reminder Service
8. **This Gateway**

## Project structure

```
MediTrackApiGateway/
├── MediTrackApiGateway.csproj   # Ocelot dependency
├── Program.cs                   # Ocelot + CORS setup
├── ocelot.json                  # Route definitions
├── appsettings.json
├── appsettings.Development.json
├── Properties/
│   └── launchSettings.json      # Port 5000
└── seed-data.http               # Local seed requests
global.json                      # SDK version policy
```

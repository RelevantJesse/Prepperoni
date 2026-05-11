# Prepperoni

A small ASP.NET Core web app for generating three role-specific interview questions without requiring an API key.

## Tech choices

- C# / ASP.NET Core minimal API
- Static HTML, CSS, and JavaScript front end
- Pollinations text generation via REST because basic usage does not require signup or an API key
- ASP.NET Core fixed-window rate limiting on the question endpoint

## Run locally

```powershell
dotnet run --project Prepperoni.Web
```

Then open the local URL printed by `dotnet run`.

From Visual Studio, open `Prepperoni.slnx`, set `Prepperoni.Web` as the startup project, and run it.

## Rate limiting

`POST /api/interview-questions` is limited to 5 requests per minute per IP address. Extra requests return HTTP 429.

## Prompt

The server asks the AI provider for exactly three questions in JSON. It gives the model startup interview context, asks for practical signal around ownership, communication, ambiguity, judgment, and role-specific skill, and explicitly avoids personal information.

## Hosting notes

The app reads `PORT` if your hosting provider sets one. A root `Dockerfile` is included for container-based hosts.

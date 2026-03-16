# Heroku and GitHub Pages Deployment Guide

This repository is configured for:

- Heroku container deployment for the ASP.NET backend (`Dockerfile`, `heroku.yml`)
- GitHub Pages deployment for the React frontend (`.github/workflows/deploy-client-pages.yml`)

## Prerequisites

- Heroku CLI installed
- Docker installed and running
- Access to the Heroku app/account
- GitHub repository settings access (Pages)

## 1) Create and configure the Heroku app

```powershell
heroku login
heroku create <your-app-name>
heroku stack:set container -a <your-app-name>
```

## 2) Configure Heroku runtime settings

```powershell
heroku config:set ASPNETCORE_ENVIRONMENT=Production -a <your-app-name>
```

If your frontend is hosted on GitHub Pages, allow that origin for API calls:

```powershell
heroku config:set Cors__AllowedOrigins__0=https://jrscott812.github.io -a <your-app-name>
```

## 3) Deploy backend from repository root

```powershell
heroku container:login
heroku container:push web -a <your-app-name>
heroku container:release web -a <your-app-name>
heroku open -a <your-app-name>
```

## 4) Configure GitHub Pages deployment

1. Push to `master` (or run workflow manually).
2. In GitHub repository settings, set Pages source to `GitHub Actions`.
3. Ensure the Pages workflow `.github/workflows/deploy-client-pages.yml` is enabled.

The workflow builds the client with:

- `VITE_BASE_PATH=/AVI-Meals/`

## 5) Point frontend API to Heroku

Set a GitHub Actions repository variable or secret named:

- `VITE_API_BASE_URL` = `https://<your-app-name>.herokuapp.com`

Then update the workflow build step to expose it (already supported in app code):

```yaml
env:
  VITE_BASE_PATH: /AVI-Meals/
  VITE_API_BASE_URL: ${{ vars.VITE_API_BASE_URL }}
```

The client falls back to `/api/meals` when `VITE_API_BASE_URL` is not set, which is correct for same-origin hosting.

## 6) Runtime notes

- The container entrypoint binds ASP.NET to Heroku's dynamic port (`PORT`).
- Forwarded headers are enabled in `Program.cs` so HTTPS redirection works behind Heroku's proxy.
- CORS origins are read from `Cors:AllowedOrigins`.
- Client assets are built in the Docker client build stage and included in the published server output.

## 7) Verify deployment

- Heroku API: `GET https://<your-app-name>.herokuapp.com/api/meals`
- GitHub Pages frontend: `https://jrscott812.github.io/AVI-Meals/`

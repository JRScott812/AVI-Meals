/// <reference types="vitest/config" />
import { fileURLToPath, URL } from 'node:url';

import { defineConfig } from 'vite';
import plugin from '@vitejs/plugin-react';
import fs from 'fs';
import path from 'path';
import child_process from 'child_process';
import { env } from 'process';

const baseFolder =
	env.APPDATA !== undefined && env.APPDATA !== ''
		? `${env.APPDATA}/ASP.NET/https`
		: `${env.HOME}/.aspnet/https`;

const certificateName = 'avi-meals.client';
const certFilePath = path.join(baseFolder, `${certificateName}.pem`);
const keyFilePath = path.join(baseFolder, `${certificateName}.key`);

/**
 * Ensures local development certificates exist for HTTPS proxy mode.
 */
function ensureDevCertificateFiles(): void {
	if (!fs.existsSync(baseFolder)) {
		fs.mkdirSync(baseFolder, { recursive: true });
	}

	if (!fs.existsSync(certFilePath) || !fs.existsSync(keyFilePath)) {
		if (0 !== child_process.spawnSync('dotnet', [
			'dev-certs',
			'https',
			'--export-path',
			certFilePath,
			'--format',
			'Pem',
			'--no-password'
		], { stdio: 'inherit' }).status) {
			throw new Error('Could not create certificate.');
		}
	}
}

const target = env.ASPNETCORE_HTTPS_PORT ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}` :
	env.ASPNETCORE_URLS ? env.ASPNETCORE_URLS.split(';')[0] : 'https://localhost:7158';

/**
 * Resolves the hosted base path used by static builds (e.g., GitHub Pages).
 */
function getConfiguredBasePath(): string {
	const configuredBasePath = env.VITE_BASE_PATH?.trim();
	if (configuredBasePath === undefined || configuredBasePath === '') {
		return '/';
	}

	if (configuredBasePath.startsWith('/')) {
		return configuredBasePath.endsWith('/') ? configuredBasePath : `${configuredBasePath}/`;
	}

	return `/${configuredBasePath.endsWith('/') ? configuredBasePath : `${configuredBasePath}/`}`;
}

// https://vitejs.dev/config/
export default defineConfig(({ command }) => {
	const isServeCommand = command === 'serve';
	if (isServeCommand) {
		ensureDevCertificateFiles();
	}

	return {
		base: getConfiguredBasePath(),
		plugins: [plugin()],
		resolve: {
			alias: {
				'@': fileURLToPath(new URL('./src', import.meta.url))
			}
		},
		server: !isServeCommand ? undefined : {
			// Bind to IPv4 loopback to avoid permission errors when IPv6 (::1) is restricted
			host: '127.0.0.1',
			// If the requested port is unavailable, allow Vite to try the next free port
			strictPort: false,
			proxy: {
				'^/weatherforecast': {
					target,
					secure: false
				},
				'^/api': {
					target,
					secure: false
				}
			},
            // Use DEV_SERVER_PORT when set, otherwise allow Vite to pick any free port (0)
			port: env.DEV_SERVER_PORT ? parseInt(env.DEV_SERVER_PORT) : 0,
			https: {
				key: fs.readFileSync(keyFilePath),
				cert: fs.readFileSync(certFilePath)
			}
		}
	};
});

#!/usr/bin/env node
// @raptortwentyone/openapi — npm front door for the Raptor21.OpenApi generator.
//
// The generator itself is a .NET tool (Raptor21.OpenApi.Generics.Cli on nuget.org). This script installs
// that tool, at exactly this package's version, into a directory owned by this package the first time it
// runs, and forwards every argument to it afterwards. Nothing is installed globally and nothing runs at
// `npm install` time (no postinstall), so the package is inert until a script actually calls it.
//
// Usage — same flags as the .NET tool:
//   raptor21-openapi <document> -l typescript --queries --no-headers -o src/api/generated
//   raptor21-openapi --help
//
// Environment:
//   RAPTOR21_OPENAPI_VERSION   use another tool version than this package's own (escape hatch, not routine)
//   RAPTOR21_OPENAPI_TOOL_DIR  where to install the tool (default: <this package>/.tool)
//   DOTNET_ROOT / PATH         a .NET SDK (8.0 or later) must be reachable as `dotnet`

import { spawnSync } from 'node:child_process'
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const here = dirname(fileURLToPath(import.meta.url))
const pkg = JSON.parse(readFileSync(join(here, '..', 'package.json'), 'utf8'))

const TOOL_ID = 'Raptor21.OpenApi.Generics.Cli'
const COMMAND = 'raptor21-openapi'
const version = process.env.RAPTOR21_OPENAPI_VERSION || pkg.version
const toolDir = resolve(process.env.RAPTOR21_OPENAPI_TOOL_DIR || join(here, '..', '.tool'))
const stamp = join(toolDir, '.version')
const exe = join(toolDir, process.platform === 'win32' ? `${COMMAND}.exe` : COMMAND)

function fail(message) {
  console.error(`[${pkg.name}] ${message}`)
  process.exit(1)
}

function dotnetAvailable() {
  const r = spawnSync('dotnet', ['--version'], { stdio: 'pipe', encoding: 'utf8' })
  return r.status === 0 ? r.stdout.trim() : null
}

function installed() {
  return existsSync(exe) && existsSync(stamp) && readFileSync(stamp, 'utf8').trim() === version
}

function install() {
  const sdk = dotnetAvailable()
  if (!sdk) {
    fail(
      `the .NET SDK is required (\`dotnet\` was not found on PATH). Install .NET 8.0 or later from https://dot.net ` +
        `and run again; the generator is a .NET tool that this package installs into ${toolDir}.`,
    )
  }

  console.error(`[${pkg.name}] installing ${TOOL_ID} ${version} into ${toolDir} (once; .NET SDK ${sdk})`)
  mkdirSync(toolDir, { recursive: true })

  // `install` fails when a different version is already there, so an upgrade is uninstall + install.
  if (existsSync(exe)) spawnSync('dotnet', ['tool', 'uninstall', TOOL_ID, '--tool-path', toolDir], { stdio: 'ignore' })

  const r = spawnSync(
    'dotnet',
    ['tool', 'install', TOOL_ID, '--version', version, '--tool-path', toolDir, '--ignore-failed-sources'],
    { stdio: 'inherit' },
  )
  if (r.status !== 0) {
    fail(
      `could not install ${TOOL_ID} ${version}. If the version was published minutes ago nuget.org may still be indexing it; ` +
        `otherwise check https://www.nuget.org/packages/${TOOL_ID} and your NuGet sources.`,
    )
  }
  writeFileSync(stamp, version)
}

if (!installed()) install()

const run = spawnSync(exe, process.argv.slice(2), { stdio: 'inherit' })
if (run.error) fail(`failed to start ${exe}: ${run.error.message}`)
process.exit(run.status ?? 1)

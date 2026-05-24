#!/usr/bin/env node
import { spawnSync } from "node:child_process";

const args = process.argv.slice(2);
const command = args[0];

if (command !== "add" || args.length < 2) {
  console.error("Usage: npx @company/skills-hub add <slug> --hub <hub-url>");
  process.exit(1);
}

const slug = args[1];
const hubIndex = args.indexOf("--hub");
const hub = hubIndex >= 0 ? args[hubIndex + 1] : process.env.SKILLS_HUB_URL;

if (!hub) {
  console.error("--hub or SKILLS_HUB_URL is required");
  process.exit(1);
}

const baseUrl = hub.replace(/\/+$/, "");
const installUrl = `${baseUrl}/api/skills/${encodeURIComponent(slug)}/install`;
const wrapperUrl = `${baseUrl}/api/skills/${encodeURIComponent(slug)}/install/wrapper`;

const installInfo = await fetchJson(installUrl, { method: "GET" });
const nativeCommand = parseNativeCommand(installInfo.installCommand);

// 中文注释：真实安装前先记录 wrapper 入口事件；如果后续 npx skills 失败，Hub 可通过事件类型区分为 wrapper 发起而非保证成功。
await fetchJson(wrapperUrl, { method: "POST" });

const result = spawnSync(nativeCommand[0], nativeCommand.slice(1), { stdio: "inherit", shell: process.platform === "win32" });
process.exit(result.status ?? 1);

async function fetchJson(url, init) {
  const response = await fetch(url, init);
  if (!response.ok) {
    const text = await response.text();
    throw new Error(`${url} failed: ${response.status} ${text}`);
  }
  return response.json();
}

function parseNativeCommand(commandLine) {
  const parts = commandLine.match(/(?:[^\s"]+|"[^"]*")+/g)?.map((part) => part.replace(/^"|"$/g, "")) ?? [];
  if (parts[0] !== "npx" || parts[1] !== "skills" || parts[2] !== "add") {
    throw new Error(`unsupported install command: ${commandLine}`);
  }
  return parts;
}

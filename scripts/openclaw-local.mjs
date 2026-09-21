import fs from "node:fs/promises";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";

async function main() {
  const rawInput = await readStdin();

  if (!rawInput.trim()) {
    throw new Error("No request payload was provided on stdin.");
  }

  const request = JSON.parse(stripBom(rawInput));
  const config = await loadOpenClawRuntimeConfig();
  const { provider, model } = resolveProviderModel(config, request.model);
  const runEmbeddedPiAgent = await loadRunEmbeddedPiAgent();
  const tools = normalizeTools(request.tools);
  const prompt = buildPlannerPrompt(request, tools);
  const tmpDir = await fs.mkdtemp(path.join(os.tmpdir(), "jarvis-openclaw-local-"));

  try {
    const result = await runEmbeddedPiAgent({
      sessionId: normalizeString(request.sessionId) || `jarvis-${Date.now()}`,
      sessionFile: path.join(tmpDir, "session.json"),
      workspaceDir: normalizeString(request.workspaceRoot) || process.cwd(),
      config,
      prompt,
      timeoutMs: normalizeTimeout(request.timeoutMs),
      runId: `jarvis-openclaw-local-${Date.now()}`,
      provider,
      model,
      thinkLevel: normalizeThinking(request.thinking),
      disableTools: true,
      streamParams: {
        temperature: typeof request.temperature === "number" ? request.temperature : undefined
      }
    });

    const text = collectText(result?.payloads);

    if (!text) {
      throw new Error("OpenClaw returned an empty response.");
    }

    const parsed = normalizePlannerResponse(parseJsonResponse(text), tools);
    process.stdout.write(JSON.stringify({
      content: parsed.content,
      toolCalls: parsed.toolCalls,
      provider,
      model
    }));
  } finally {
    await fs.rm(tmpDir, { recursive: true, force: true });
  }
}

function buildPlannerPrompt(request, tools) {
  const messages = normalizeMessages(request.messages);
  const schema = {
    type: "object",
    properties: {
      content: { type: "string" },
      toolCalls: {
        type: "array",
        items: {
          type: "object",
          properties: {
            name: {
              type: "string",
              enum: tools.map((tool) => tool.name)
            },
            arguments: {
              type: "object",
              properties: {
                input: { type: "string" },
                purpose: { type: "string" },
                expectedEvidence: { type: "string" }
              },
              additionalProperties: false
            }
          },
          required: ["name", "arguments"],
          additionalProperties: false
        },
        ...(tools.length === 0 ? { maxItems: 0 } : {})
      }
    },
    required: ["content", "toolCalls"],
    additionalProperties: false
  };

  const instructions = [
    "You are producing the next planner turn for Jarvis.",
    "Return only JSON that matches the provided schema.",
    tools.length === 0
      ? "No tools are available for this turn, so answer directly in content and return an empty toolCalls array."
      : "If a Jarvis tool is needed before you can answer, return one or more toolCalls and keep content empty until the tool work is complete.",
    "Do not invent tool results.",
    "Do not call your own tools.",
    "Each tool call arguments object may only include the optional string fields `input`, `purpose`, and `expectedEvidence`."
  ].join(" ");

  return `${instructions}

CONVERSATION_JSON:
${JSON.stringify(messages, null, 2)}

AVAILABLE_TOOLS_JSON:
${JSON.stringify(tools, null, 2)}

RESPONSE_SCHEMA_JSON:
${JSON.stringify(schema, null, 2)}`;
}

function normalizeMessages(messages) {
  if (!Array.isArray(messages)) {
    return [];
  }

  return messages.map((message) => ({
    role: normalizeString(message?.role),
    toolCallId: normalizeString(message?.toolCallId),
    content: Array.isArray(message?.content)
      ? message.content.map((part) => normalizeContentPart(part))
      : [],
    toolCalls: Array.isArray(message?.toolCalls)
      ? message.toolCalls.map((toolCall) => ({
          id: normalizeString(toolCall?.id),
          name: normalizeString(toolCall?.name),
          argumentsJson: normalizeString(toolCall?.argumentsJson)
        }))
      : []
  }));
}

function normalizeContentPart(part) {
  if (normalizeString(part?.type) === "image_url") {
    const url = normalizeString(part?.url);
    return {
      type: "image_url",
      note: "Image inputs are not supported by openclaw-local. Use a fallback provider for vision turns.",
      urlPreview: url.slice(0, 160),
      truncated: url.length > 160
    };
  }

  return {
    type: "text",
    text: normalizeString(part?.text)
  };
}

function normalizeTools(tools) {
  if (!Array.isArray(tools)) {
    return [];
  }

  return tools
    .map((tool) => ({
      name: normalizeString(tool?.name),
      description: normalizeString(tool?.description),
      inputDescription: normalizeString(tool?.inputDescription),
      purposeDescription: normalizeString(tool?.purposeDescription),
      expectedEvidenceDescription: normalizeString(tool?.expectedEvidenceDescription)
    }))
    .filter((tool) => tool.name);
}

function normalizePlannerResponse(value, tools) {
  if (!value || typeof value !== "object" || Array.isArray(value)) {
    throw new Error("OpenClaw did not return a JSON object.");
  }

  const allowedToolNames = new Set(tools.map((tool) => tool.name));
  const content = typeof value.content === "string" ? value.content : "";
  const rawToolCalls = Array.isArray(value.toolCalls) ? value.toolCalls : [];
  const toolCalls = [];

  for (const rawToolCall of rawToolCalls) {
    if (!rawToolCall || typeof rawToolCall !== "object" || Array.isArray(rawToolCall)) {
      throw new Error("OpenClaw returned an invalid tool call.");
    }

    const name = normalizeString(rawToolCall.name);

    if (!name) {
      throw new Error("OpenClaw returned a tool call without a name.");
    }

    if (!allowedToolNames.has(name)) {
      throw new Error(`OpenClaw returned an unknown Jarvis tool: ${name}`);
    }

    let args = rawToolCall.arguments;

    if (args == null) {
      args = {};
    }

    if (typeof args !== "object" || Array.isArray(args)) {
      throw new Error(`OpenClaw returned invalid arguments for tool ${name}.`);
    }

    const normalizedArgs = {};

    if (Object.hasOwn(args, "input") && args.input != null) {
      normalizedArgs.input = typeof args.input === "string"
        ? args.input
        : String(args.input);
    }

    if (Object.hasOwn(args, "purpose") && args.purpose != null) {
      normalizedArgs.purpose = typeof args.purpose === "string"
        ? args.purpose
        : String(args.purpose);
    }

    if (Object.hasOwn(args, "expectedEvidence") && args.expectedEvidence != null) {
      normalizedArgs.expectedEvidence = typeof args.expectedEvidence === "string"
        ? args.expectedEvidence
        : String(args.expectedEvidence);
    }

    toolCalls.push({
      name,
      arguments: normalizedArgs
    });
  }

  return { content, toolCalls };
}

function parseJsonResponse(text) {
  const cleaned = stripCodeFences(normalizeString(text));

  try {
    return JSON.parse(cleaned);
  } catch (error) {
    throw new Error(`OpenClaw returned invalid JSON: ${error instanceof Error ? error.message : String(error)}`);
  }
}

function stripCodeFences(text) {
  const trimmed = text.trim();
  const match = trimmed.match(/^```(?:json)?\s*([\s\S]*?)\s*```$/i);
  return match ? (match[1] ?? "").trim() : trimmed;
}

function collectText(payloads) {
  if (!Array.isArray(payloads)) {
    return "";
  }

  return payloads
    .filter((payload) => !payload?.isError && typeof payload?.text === "string")
    .map((payload) => payload.text)
    .join("\n")
    .trim();
}

async function loadRunEmbeddedPiAgent() {
  const openClawRoot = resolveOpenClawRoot();
  const runtimePath = path.join(openClawRoot, "dist", "pi-embedded.runtime.js");
  const runtimeModule = await import(pathToFileURL(runtimePath).href);

  if (typeof runtimeModule.runEmbeddedPiAgent !== "function") {
    throw new Error(`runEmbeddedPiAgent export was not found in ${runtimePath}`);
  }

  return runtimeModule.runEmbeddedPiAgent;
}

async function loadOpenClawRuntimeConfig() {
  const openClawRoot = resolveOpenClawRoot();
  const ioModulePath = path.join(openClawRoot, "dist", "io-CS2J_l4V.js");
  const ioModule = await import(pathToFileURL(ioModulePath).href);

  if (typeof ioModule.a !== "function") {
    throw new Error(`loadConfig export was not found in ${ioModulePath}`);
  }

  return ioModule.a();
}

function resolveOpenClawRoot() {
  const explicit = normalizeString(process.env.OPENCLAW_INSTALL_DIR);

  if (explicit) {
    return explicit;
  }

  const appData = normalizeString(process.env.APPDATA);

  if (!appData) {
    throw new Error("APPDATA is not set and OPENCLAW_INSTALL_DIR was not provided.");
  }

  return path.join(appData, "npm", "node_modules", "openclaw");
}

function resolveProviderModel(config, requestedModel) {
  const defaults = splitModelRef(
    typeof config?.agents?.defaults?.model === "string"
      ? config.agents.defaults.model
      : typeof config?.agents?.defaults?.model?.primary === "string"
        ? config.agents.defaults.model.primary
        : ""
  );
  const requested = splitModelRef(normalizeString(requestedModel));
  const provider = requested.provider || defaults.provider;
  const model = requested.model || defaults.model;

  if (!provider || !model) {
    throw new Error("OpenClaw provider/model could not be resolved from the request or config.");
  }

  return { provider, model };
}

function splitModelRef(value) {
  const trimmed = normalizeString(value);

  if (!trimmed) {
    return { provider: "", model: "" };
  }

  const slashIndex = trimmed.indexOf("/");

  if (slashIndex < 0) {
    return { provider: "", model: trimmed };
  }

  return {
    provider: trimmed.slice(0, slashIndex).trim(),
    model: trimmed.slice(slashIndex + 1).trim()
  };
}

function normalizeThinking(value) {
  const normalized = normalizeString(value).toLowerCase();
  const allowed = new Set(["off", "minimal", "low", "medium", "high", "adaptive", "xhigh"]);
  return allowed.has(normalized) ? normalized : "medium";
}

function normalizeTimeout(value) {
  return typeof value === "number" && Number.isFinite(value) && value > 0
    ? Math.trunc(value)
    : 120000;
}

function normalizeString(value) {
  return typeof value === "string" ? value.trim() : "";
}

function stripBom(text) {
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

async function readStdin() {
  const chunks = [];

  for await (const chunk of process.stdin) {
    chunks.push(chunk);
  }

  return chunks.join("");
}

main().catch((error) => {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
});

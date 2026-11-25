import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import { readFile } from 'node:fs/promises';
import ts from 'typescript';

const require = createRequire(import.meta.url);
const React = require('react');
const { renderToStaticMarkup } = require('react-dom/server');

async function loadComponent(path, requireModule = require) {
  const source = await readFile(path, 'utf8');
  const { outputText } = ts.transpileModule(source, { compilerOptions: { module: ts.ModuleKind.CommonJS, jsx: ts.JsxEmit.ReactJSX, target: ts.ScriptTarget.ES2022 } });
  const module = { exports: {} };
  new Function('module', 'exports', 'require', outputText)(module, module.exports, requireModule);
  return module.exports;
}

test('session cost displays dollars to four decimal places', async () => {
  const { SessionCost } = await loadComponent('src/components/SessionCost.tsx');
  assert.equal(renderToStaticMarkup(React.createElement(SessionCost, { cost: 0.12567 })), '<span class="session-cost" title="AI credits / 100">Session cost: $0.1257</span>');
});

function createHarness() {
  const slots = [];
  let index = 0;
  const effects = new Map();
  const react = {
    useState(initial) {
      const slot = index++;
      if (!(slot in slots)) slots[slot] = typeof initial === 'function' ? initial() : initial;
      return [slots[slot], (value) => { slots[slot] = typeof value === 'function' ? value(slots[slot]) : value; }];
    },
    useRef(initial) {
      const slot = index++;
      if (!(slot in slots)) slots[slot] = { current: initial };
      return slots[slot];
    },
    useEffect(effect, deps) {
      const slot = index++;
      const old = effects.get(slot);
      if (!old || !deps || deps.some((value, i) => value !== old.deps[i])) {
        old?.cleanup?.();
        effects.set(slot, { deps, cleanup: effect() });
      }
    },
  };
  const handlers = new Map();
  let stream;
  const connection = {
    invoke: async (method) => method === 'GetRepositories' ? [] : undefined,
    stream: () => ({ subscribe: (callbacks) => { stream = callbacks; return { dispose() {} }; } }),
    on: (name, handler) => handlers.set(name, handler),
    off: (name) => handlers.delete(name),
  };
  const SessionCost = function SessionCost() {};
  const jsx = (type, props) => ({ type, props });
  const modules = {
    react,
    'react/jsx-runtime': { jsx, jsxs: jsx, Fragment: Symbol('Fragment') },
    '../hub': { connection, errorMessage: String },
    '../components/Markdown': { Markdown: function Markdown() {} },
    '../components/ModelPicker': { ModelPicker: function ModelPicker() {}, defaultModelSettings: {}, defaultPlanModelSettings: {} },
    '../components/SessionCost': { SessionCost },
  };
  return {
    render(component) { index = 0; return component({ connected: true }); },
    requireModule(name) { if (name in modules) return modules[name]; return require(name); },
    getStream: () => stream,
    handlers,
  };
}

function find(node, predicate) {
  if (!node || typeof node !== 'object') return undefined;
  if (Array.isArray(node)) {
    for (const child of node) { const result = find(child, predicate); if (result) return result; }
    return undefined;
  }
  if (predicate(node)) return node;
  return find(node.props?.children, predicate);
}

const cost = (tree) => find(tree, (node) => typeof node.type === 'function' && node.type.name === 'SessionCost');
const button = (tree, text) => find(tree, (node) => node.type === 'button' && node.props.children === text);
const messageEntries = (tree) => {
  const messages = find(tree, (node) => node.props?.className === 'messages');
  return messages.props.children.flat().filter((node) => /^message message-(user|assistant|tool|error)$/.test(node?.props?.className ?? ''));
};

test('chat starts, updates and resets session cost without adding usage as text', async () => {
  const harness = createHarness();
  const { ChatView } = await loadComponent('src/views/ChatView.tsx', harness.requireModule);
  let tree = harness.render(ChatView);
  assert.equal(cost(tree), undefined);
  find(tree, (node) => node.type === 'textarea').props.onChange({ target: { value: 'hello' } });
  tree = harness.render(ChatView);
  await button(tree, 'Send').props.onClick();
  tree = harness.render(ChatView);
  assert.equal(cost(tree)?.props.cost, 0);
  assert.equal(messageEntries(tree).length, 1);
  harness.getStream().next({ kind: 'usage', text: '', sessionCost: 0.02 });
  tree = harness.render(ChatView);
  assert.equal(cost(tree)?.props.cost, 0.02);
  assert.equal(messageEntries(tree).length, 1);
  assert.equal(find(tree, (node) => typeof node.type === 'function' && node.type.name === 'Markdown'), undefined);
  harness.getStream().next({ kind: 'text', text: 'Hi' });
  harness.getStream().next({ kind: 'usage', text: '', sessionCost: 0.05 });
  harness.getStream().next({ kind: 'text', text: '!' });
  tree = harness.render(ChatView);
  assert.equal(cost(tree)?.props.cost, 0.05);
  assert.equal(messageEntries(tree).length, 2);
  assert.equal(find(tree, (node) => typeof node.type === 'function' && node.type.name === 'Markdown')?.props.text, 'Hi!');
  harness.getStream().next({ kind: 'usage', text: '', sessionCost: null });
  tree = harness.render(ChatView);
  assert.equal(cost(tree)?.props.cost, 0);
  button(tree, 'New chat').props.onClick();
  tree = harness.render(ChatView);
  assert.equal(cost(tree), undefined);
  find(tree, (node) => node.type === 'textarea').props.onChange({ target: { value: 'again' } });
  tree = harness.render(ChatView);
  await button(tree, 'Send').props.onClick();
  assert.equal(cost(harness.render(ChatView))?.props.cost, 0);
});

test('plan session cost updates, remains after all outcomes and resets on restart', async () => {
  globalThis.localStorage = { getItem: () => '', setItem() {} };
  const harness = createHarness();
  const { PlanView } = await loadComponent('src/views/PlanView.tsx', harness.requireModule);
  let tree = harness.render(PlanView);
  assert.equal(cost(tree), undefined);
  assert.equal(typeof harness.handlers.get('PlanUsage'), 'function');
  find(tree, (node) => node.type === 'input' && node.props.placeholder?.startsWith('Absolute path')).props.onChange({ target: { value: 'D:\\dev\\Example' } });
  find(tree, (node) => node.type === 'textarea' && node.props.placeholder === 'Describe the feature to plan').props.onChange({ target: { value: 'Example feature' } });
  tree = harness.render(PlanView);
  await button(tree, 'Start planning').props.onClick();
  tree = harness.render(PlanView);
  assert.equal(cost(tree)?.props.cost, 0);
  const phaseRow = find(tree, (node) => node.props?.className === 'phase-row');
  assert.ok(find(phaseRow, (node) => node.type === 'ol' && node.props.className === 'phases'));
  assert.ok(cost(phaseRow));
  for (const [outcome, args] of [['PlanCompleted', [[]]], ['PlanFailed', ['example']], ['PlanCancelled', []]]) {
    harness.handlers.get('PlanUsage')(0.01, 0.03);
    tree = harness.render(PlanView);
    assert.equal(cost(tree)?.props.cost, 0.03);
    harness.handlers.get(outcome)(...args);
    tree = harness.render(PlanView);
    assert.equal(cost(tree)?.props.cost, 0.03);
    await button(tree, 'Plan again').props.onClick();
    tree = harness.render(PlanView);
    assert.equal(cost(tree)?.props.cost, 0);
  }
  delete globalThis.localStorage;
});

import { useEffect, useRef, useState } from 'react';
import type { ISubscription } from '@microsoft/signalr';
import { connection, errorMessage } from '../hub';
import type { ChatChunk } from '../types';
import { Markdown } from '../components/Markdown';
import { SessionCost } from '../components/SessionCost';
import { defaultModelSettings, ModelPicker, type ModelSettings } from '../components/ModelPicker';

type Entry = { id: number; role: 'user' | 'assistant' | 'tool' | 'error'; text: string };

let nextId = 0;

export function ChatView({ connected }: { connected: boolean }) {
  const [productionMode, setProductionMode] = useState(true);
  const [settings, setSettings] = useState<ModelSettings>(defaultModelSettings);
  const [started, setStarted] = useState(false);
  const [sessionCost, setSessionCost] = useState<number | null>(null);
  const [entries, setEntries] = useState<Entry[]>([]);
  const [input, setInput] = useState('');
  const [busy, setBusy] = useState(false);
  const subscription = useRef<ISubscription<ChatChunk> | null>(null);
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' });
  }, [entries]);
  useEffect(() => () => {
    subscription.current?.dispose();
  }, []);

  function append(role: Entry['role'], text: string) {
    const entry = { id: nextId++, role, text };
    setEntries((e) => [...e, entry]);
  }

  function appendReply(text: string) {
    const id = nextId++;
    setEntries((e) => {
      const last = e.at(-1);
      return last?.role === 'assistant'
        ? [...e.slice(0, -1), { ...last, text: last.text + text }]
        : [...e, { id, role: 'assistant', text }];
    });
  }

  async function send() {
    const message = input.trim();
    if (!message || busy) {
      return;
    }

    setInput('');
    setBusy(true);
    append('user', message);
    if (!started) {
      try {
        await connection.invoke('StartChat', { productionMode, ...settings });
        setStarted(true);
        setSessionCost(0);
      } catch (err) {
        append('error', errorMessage(err));
        setBusy(false);
        return;
      }
    }

    subscription.current = connection.stream<ChatChunk>('SendMessage', message).subscribe({
      next: (chunk) => {
        if (chunk.kind === 'usage') {
          setSessionCost(chunk.sessionCost ?? 0);
        } else if (chunk.kind === 'tool') {
          append('tool', `${chunk.toolName}: ${chunk.text}`);
        } else {
          appendReply(chunk.text);
        }
      },
      complete: () => {
        subscription.current = null;
        setBusy(false);
      },
      error: (err) => {
        subscription.current = null;
        append('error', errorMessage(err));
        setBusy(false);
      },
    });
  }

  function stop() {
    subscription.current?.dispose();
    subscription.current = null;
    setBusy(false);
  }

  function newChat() {
    stop();
    setStarted(false);
    setSessionCost(null);
    setEntries([]);
  }

  return (
    <div className="chat">
      <div className="toolbar">
        <label>
          Mode
          <select value={productionMode ? 'production' : 'full'} disabled={started} onChange={(e) => setProductionMode(e.target.value === 'production')}>
            <option value="production">Production mode</option>
            <option value="full">Full control</option>
          </select>
        </label>
        <ModelPicker value={settings} onChange={setSettings} disabled={started} />
        <button onClick={newChat} disabled={!started && entries.length === 0}>
          New chat
        </button>
        {sessionCost !== null && <SessionCost cost={sessionCost} />}
      </div>
      <div className="messages">
        {entries.length === 0 && <p className="hint">Ask anything. The assistant can read any file on this machine but cannot modify anything.</p>}
        {entries.map((entry) => (
          <div key={entry.id} className={`message message-${entry.role}`}>
            {entry.role === 'assistant' ? <Markdown text={entry.text} /> : entry.text}
          </div>
        ))}
        {busy && entries.at(-1)?.role !== 'assistant' && <div className="message message-thinking">Thinking...</div>}
        <div ref={bottom} />
      </div>
      <div className="composer">
        <textarea
          value={input}
          placeholder="Message (Enter to send, Shift+Enter for a new line)"
          rows={3}
          onChange={(e) => setInput(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
              e.preventDefault();
              send();
            }
          }}
        />
        {busy ? (
          <button onClick={stop}>Stop</button>
        ) : (
          <button className="primary" onClick={send} disabled={!connected || !input.trim()}>
            Send
          </button>
        )}
      </div>
    </div>
  );
}

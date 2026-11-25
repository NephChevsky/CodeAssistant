import { useEffect, useRef, useState } from 'react';
import { connection, errorMessage } from '../hub';
import type { PendingQuestion, PlanningPhase, RepositoryInfo, SavedPlan } from '../types';
import { Markdown } from '../components/Markdown';
import { defaultModelSettings, ModelPicker, type ModelSettings } from '../components/ModelPicker';

type Entry =
  | { id: number; kind: 'message'; markdown: string }
  | { id: number; kind: 'tool'; name: string; summary: string }
  | { id: number; kind: 'question'; question: PendingQuestion; answer?: string }
  | { id: number; kind: 'review'; title: string; approveLabel: string; markdown: string; verdict?: string }
  | { id: number; kind: 'status'; tone: 'info' | 'success' | 'error'; text: string };

type Status = 'idle' | 'running' | 'finished';

const phases: PlanningPhase[] = ['discovery', 'alignment', 'design', 'refinement', 'completed'];
const repositoryKey = 'codeassistant.plan.repositoryRoot';

let nextId = 0;

function capitalize(text: string) {
  return text.charAt(0).toUpperCase() + text.slice(1);
}

export function PlanView({ connected }: { connected: boolean }) {
  const [repositoryRoot, setRepositoryRoot] = useState(() => localStorage.getItem(repositoryKey) ?? '');
  const [repositories, setRepositories] = useState<RepositoryInfo[]>([]);
  const [description, setDescription] = useState('');
  const [settings, setSettings] = useState<ModelSettings>(defaultModelSettings);
  const [status, setStatus] = useState<Status>('idle');
  const [phase, setPhase] = useState<PlanningPhase | null>(null);
  const [entries, setEntries] = useState<Entry[]>([]);
  const [error, setError] = useState<string | null>(null);
  const bottom = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottom.current?.scrollIntoView({ block: 'end' });
  }, [entries.length]);

  useEffect(() => {
    if (!connected) {
      return;
    }
    connection
      .invoke<RepositoryInfo[]>('GetRepositories')
      .then((found) => {
        setRepositories(found);
        if (found.length > 0) {
          setRepositoryRoot((current) => found.find((r) => r.path.toLowerCase() === current.trim().toLowerCase())?.path ?? '');
        }
      })
      .catch((err) => setError(errorMessage(err)));
  }, [connected]);

  useEffect(() => {
    const add = (entry: Entry) => setEntries((e) => [...e, entry]);
    const finish = (tone: 'info' | 'success' | 'error', text: string) => {
      add({ id: nextId++, kind: 'status', tone, text });
      setStatus('finished');
    };

    const handlers: [string, (...args: any[]) => void][] = [
      ['PlanPhase', (p: PlanningPhase) => setPhase(p)],
      ['PlanMessage', (markdown: string) => add({ id: nextId++, kind: 'message', markdown })],
      ['PlanTool', (name: string, summary: string) => add({ id: nextId++, kind: 'tool', name, summary })],
      ['PlanQuestion', (question: PendingQuestion) => add({ id: nextId++, kind: 'question', question })],
      ['PhaseReview', (p: PlanningPhase, markdown: string) =>
        add({ id: nextId++, kind: 'review', title: `${capitalize(p)} complete. Agree to move on?`, approveLabel: 'Continue', markdown })],
      ['PlanReview', (markdown: string) =>
        add({ id: nextId++, kind: 'review', title: 'Plan for review', approveLabel: 'Approve and save', markdown })],
      ['PlanCompleted', (saved: SavedPlan[]) =>
        finish('success', `Saved ${saved.length} plan${saved.length === 1 ? '' : 's'}:\n${saved.map((s) => `${s.markdownPath} and ${s.jsonPath}`).join('\n')}`)],
      ['PlanFailed', (message: string) => finish('error', `Planning failed: ${message}`)],
      ['PlanCancelled', () => finish('info', 'Planning cancelled.')],
    ];
    handlers.forEach(([name, handler]) => connection.on(name, handler));
    return () => handlers.forEach(([name, handler]) => connection.off(name, handler));
  }, []);

  function update(id: number, change: Partial<{ answer: string; verdict: string }>) {
    setEntries((e) => e.map((entry) => (entry.id === id ? ({ ...entry, ...change } as Entry) : entry)));
  }

  async function start() {
    setError(null);
    setEntries([]);
    setPhase(null);
    setStatus('running');
    try {
      await connection.invoke('StartPlan', { description, repositoryRoot, ...settings });
      localStorage.setItem(repositoryKey, repositoryRoot.trim());
    } catch (err) {
      setStatus('idle');
      setError(errorMessage(err));
    }
  }

  async function cancel() {
    try {
      await connection.invoke('CancelPlan');
    } catch (err) {
      setError(errorMessage(err));
    }
  }

  const running = status === 'running';

  return (
    <div className="plan">
      {running && (
        <div className="toolbar">
          <button onClick={cancel}>Cancel</button>
        </div>
      )}
      <div className="plan-form" hidden={running}>
        <label className="wide">
          Repository
          {repositories.length > 0 ? (
            <select value={repositoryRoot} disabled={running} onChange={(e) => setRepositoryRoot(e.target.value)}>
              <option value="">Multi-Repository</option>
              {repositories.map((r) => (
                <option key={r.path} value={r.path}>
                  {r.name}
                </option>
              ))}
            </select>
          ) : (
            <input
              value={repositoryRoot}
              placeholder="Absolute path of the repository, e.g. D:\dev\MyProject"
              disabled={running}
              onChange={(e) => setRepositoryRoot(e.target.value)}
            />
          )}
        </label>
        <label className="wide">
          Feature
          <textarea
            value={description}
            rows={4}
            placeholder="Describe the feature to plan"
            disabled={running}
            onChange={(e) => setDescription(e.target.value)}
          />
        </label>
        <div className="toolbar">
          <ModelPicker value={settings} onChange={setSettings} disabled={running} />
          <button className="primary" onClick={start} disabled={!connected || (repositories.length === 0 && !repositoryRoot.trim()) || !description.trim()}>
            {status === 'finished' ? 'Plan again' : 'Start planning'}
          </button>
        </div>
        {error && <p className="error">{error}</p>}
      </div>

      {status !== 'idle' && (
        <ol className="phases">
          {phases.map((p) => (
            <li key={p} className={phase === p ? 'current' : phase && phases.indexOf(p) < phases.indexOf(phase) ? 'done' : ''}>
              {p}
            </li>
          ))}
        </ol>
      )}

      <div className="timeline">
        {entries.map((entry) => {
          switch (entry.kind) {
            case 'message':
              return (
                <div key={entry.id} className="message message-assistant">
                  <Markdown text={entry.markdown} />
                </div>
              );
            case 'tool':
              return (
                <div key={entry.id} className="message message-tool">
                  {entry.name}: {entry.summary}
                </div>
              );
            case 'question':
              return <QuestionCard key={entry.id} entry={entry} active={running} onAnswered={(answer) => update(entry.id, { answer })} />;
            case 'review':
              return <ReviewCard key={entry.id} entry={entry} active={running} onReviewed={(verdict) => update(entry.id, { verdict })} />;
            case 'status':
              return (
                <div key={entry.id} className={`message message-status-${entry.tone}`}>
                  {entry.text}
                </div>
              );
          }
        })}
        {running && <div className="message message-thinking">Working...</div>}
        <div ref={bottom} />
      </div>
    </div>
  );
}

type QuestionProps = {
  entry: Extract<Entry, { kind: 'question' }>;
  active: boolean;
  onAnswered: (answer: string) => void;
};

function QuestionCard({ entry, active, onAnswered }: QuestionProps) {
  const { question } = entry;
  const [text, setText] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function answer(value: string) {
    setSending(true);
    setError(null);
    try {
      await connection.invoke('AnswerQuestion', question.id, value);
      onAnswered(value);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSending(false);
    }
  }

  const pending = entry.answer === undefined && active;

  return (
    <div className="card">
      <p className="card-title">{question.question}</p>
      {entry.answer !== undefined ? (
        <p className="answer">{entry.answer || '(skipped)'}</p>
      ) : (
        pending && (
          <>
            {question.choices.length > 0 && (
              <div className="choices">
                {question.choices.map((choice) => (
                  <button key={choice} disabled={sending} onClick={() => answer(choice)}>
                    {choice}
                  </button>
                ))}
              </div>
            )}
            <div className="inline-form">
              {question.allowFreeform && (
                <input
                  value={text}
                  placeholder="Your answer"
                  disabled={sending}
                  onChange={(e) => setText(e.target.value)}
                  onKeyDown={(e) => e.key === 'Enter' && text.trim() && answer(text.trim())}
                />
              )}
              {question.allowFreeform && (
                <button className="primary" disabled={sending || !text.trim()} onClick={() => answer(text.trim())}>
                  Answer
                </button>
              )}
              <button disabled={sending} onClick={() => answer('')}>
                Skip
              </button>
            </div>
          </>
        )
      )}
      {error && <p className="error">{error}</p>}
    </div>
  );
}

type ReviewProps = {
  entry: Extract<Entry, { kind: 'review' }>;
  active: boolean;
  onReviewed: (verdict: string) => void;
};

function ReviewCard({ entry, active, onReviewed }: ReviewProps) {
  const [feedback, setFeedback] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function review(approved: boolean) {
    setSending(true);
    setError(null);
    try {
      await connection.invoke('ReviewPlan', approved, approved ? null : feedback.trim());
      onReviewed(approved ? 'Agreed' : `Changes requested: ${feedback.trim()}`);
    } catch (err) {
      setError(errorMessage(err));
    } finally {
      setSending(false);
    }
  }

  return (
    <div className="card review">
      <p className="card-title">{entry.title}</p>
      <div className="plan-document">
        <Markdown text={entry.markdown} />
      </div>
      {entry.verdict !== undefined ? (
        <p className="answer">{entry.verdict}</p>
      ) : (
        active && (
          <div className="review-actions">
            <textarea value={feedback} rows={3} placeholder="What should change?" disabled={sending} onChange={(e) => setFeedback(e.target.value)} />
            <div className="inline-form">
              <button disabled={sending || !feedback.trim()} onClick={() => review(false)}>
                Request changes
              </button>
              <button className="primary" disabled={sending} onClick={() => review(true)}>
                {entry.approveLabel}
              </button>
            </div>
          </div>
        )
      )}
      {error && <p className="error">{error}</p>}
    </div>
  );
}

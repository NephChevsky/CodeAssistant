import { useEffect, useState } from 'react';
import { connection, ensureStarted, errorMessage } from './hub';
import { ChatView } from './views/ChatView';
import { PlanView } from './views/PlanView';

type Tab = 'chat' | 'plan';
type Status = 'connecting' | 'connected' | 'reconnecting' | 'disconnected';

export function App() {
  const [tab, setTab] = useState<Tab>('chat');
  const [status, setStatus] = useState<Status>('connecting');
  // The server drops chats and plans with the connection; views are remounted on a new connection.
  const [epoch, setEpoch] = useState(0);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    connection.onreconnecting(() => setStatus('reconnecting'));
    connection.onreconnected(() => {
      setStatus('connected');
      setEpoch((e) => e + 1);
      setNotice('The connection was interrupted, so the server dropped the current chat and plan.');
    });
    connection.onclose(() => setStatus('disconnected'));
    connect();
  }, []);

  function connect() {
    setStatus('connecting');
    ensureStarted().then(
      () => setStatus('connected'),
      (err) => {
        setStatus('disconnected');
        setNotice(`Could not connect to the server: ${errorMessage(err)}`);
      },
    );
  }

  function reconnect() {
    setNotice(null);
    setEpoch((e) => e + 1);
    connect();
  }

  const connected = status === 'connected';

  return (
    <div className="app">
      <header className="app-header">
        <h1>CodeAssistant</h1>
        <nav>
          <button className={tab === 'chat' ? 'tab active' : 'tab'} onClick={() => setTab('chat')}>
            Chat
          </button>
          <button className={tab === 'plan' ? 'tab active' : 'tab'} onClick={() => setTab('plan')}>
            Plan feature
          </button>
        </nav>
        <span className={`status status-${status}`}>{status}</span>
        {status === 'disconnected' && <button onClick={reconnect}>Reconnect</button>}
      </header>
      {notice && (
        <div className="notice">
          {notice}
          <button className="link" onClick={() => setNotice(null)}>
            Dismiss
          </button>
        </div>
      )}
      <main>
        <div className="view" hidden={tab !== 'chat'}>
          <ChatView key={`chat-${epoch}`} connected={connected} />
        </div>
        <div className="view" hidden={tab !== 'plan'}>
          <PlanView key={`plan-${epoch}`} connected={connected} />
        </div>
      </main>
    </div>
  );
}

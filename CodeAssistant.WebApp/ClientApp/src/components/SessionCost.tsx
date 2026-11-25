export function SessionCost({ cost }: { cost: number }) {
  return <span className="session-cost" title="AI credits / 100">Session cost: ${cost.toFixed(4)}</span>;
}

export type ChatChunk = {
  kind: 'text' | 'tool';
  text: string;
  toolName?: string | null;
};

export type PlanningPhase = 'discovery' | 'alignment' | 'design' | 'refinement' | 'completed';

export type PendingQuestion = {
  id: string;
  question: string;
  choices: string[];
  allowFreeform: boolean;
};

export type SavedPlan = {
  markdownPath: string;
  jsonPath: string;
};

export const models = ['claude-sonnet-5.5', 'claude-opus-5.5'];

export const reasoningEfforts = ['low', 'medium', 'high'];

export function defaultReasoningEffort(model: string): string {
  return model === 'claude-opus-5.5' ? 'high' : 'medium';
}

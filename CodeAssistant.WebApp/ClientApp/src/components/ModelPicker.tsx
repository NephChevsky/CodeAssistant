import { defaultReasoningEffort, models, reasoningEfforts } from '../types';

export type ModelSettings = {
  model: string;
  reasoningEffort: string;
};

export const defaultModelSettings: ModelSettings = { model: models[0], reasoningEffort: defaultReasoningEffort(models[0]) };

type Props = {
  value: ModelSettings;
  onChange: (value: ModelSettings) => void;
  disabled?: boolean;
};

export function ModelPicker({ value, onChange, disabled }: Props) {
  return (
    <>
      <label>
        Model
        <select
          value={value.model}
          disabled={disabled}
          onChange={(e) => onChange({ model: e.target.value, reasoningEffort: defaultReasoningEffort(e.target.value) })}
        >
          {models.map((m) => (
            <option key={m}>{m}</option>
          ))}
        </select>
      </label>
      <label>
        Reasoning
        <select
          value={value.reasoningEffort}
          disabled={disabled}
          onChange={(e) => onChange({ ...value, reasoningEffort: e.target.value })}
        >
          {reasoningEfforts.map((r) => (
            <option key={r}>{r}</option>
          ))}
        </select>
      </label>
    </>
  );
}

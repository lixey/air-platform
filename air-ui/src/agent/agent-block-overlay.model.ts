import type { ReportChartDto, ReportTableDto } from '../core/generated/contracts';

export interface AgentBlockOverlayModel {
  kind: 'AgentBlockOverlay';
  title: string;
  chart?: ReportChartDto | null;
  table?: ReportTableDto | null;
}

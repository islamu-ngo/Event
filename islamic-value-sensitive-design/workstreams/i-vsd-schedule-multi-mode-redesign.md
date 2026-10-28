# Multi-Mode Schedule & Agenda Redesign — I-VSD Assessment

Last Updated: 2026-10-20 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject / Workstream: schedule-multi-mode-redesign
- Report kind: provider-responsibility feature assessment
- Report revision: `r1` (initial planning intake)
- Report status: current
- Disposition: plan-aligned (all findings mapped to concrete architectural contracts and verification criteria)
- Evidence cutoff: 2026-10-20
- Reviewed input: user request for multi-mode schedule UX matching Tectonic Event app quality, comprehensive web and codebase research, and repository domain foundations in `Explore.Domain`, `Explore.Application`, and `Explore.Blazor.Client`.
- Supersedes: none

---

## 1. Executive Summary & Ethical Context

The Schedule and Program Agenda represents the central operational coordination surface for event organizers and attendees. In community and Islamic cultural gatherings, an event schedule is not merely a secular timetable; it directly orchestrates communal religious obligations (congregational prayers / *Salah*), respects the finite time and trust of participants (*Amanah* and *Waqt*), facilitates universal ease and inclusion (*Yusr* and *Istita'ah*), and protects the privacy of individual participant itineraries (*Hifz al-Khasiyyah*).

This assessment analyzes the transition from our current fragmented, auto-selected schedule components (`AgendaMillerColumns`, orphan `EventAgendaGrid`, and N+1 query `EventSessionManager`) to a unified, multi-mode schedule system (List, Grid/Agenda, Timeline, Track Focus, Compact). The redesigned architecture empowers attendees with full layout autonomy, responsive device-appropriate defaults (phone -> List, desktop -> Grid), prayer time prominence, local-first personal itineraries, and WCAG 2.2 AA accessibility.

---

## 2. Applicable Principles & Stakeholder Analysis

### 2.1 Core Principles

1. **Waqt & Amanah (Respect for Time and Trust)**:
   - Presenting an accurate, predictable, and clear schedule honors the attendee's investment of time. Ambiguous timings, hidden room locations, or conflicting session times violate trust and induce unnecessary friction.
2. **Ibadah Synchronization (Sanctity of Prayer Times)**:
   - Congregational prayers (*Fajr*, *Dhuhr*, *Asr*, *Maghrib*, *Isha*, and Friday *Jumu'ah*) must never be treated as second-class "filler" items. The schedule must prominently anchor prayer times across all display modes and provide visual awareness of session-prayer adjacency.
3. **Yusr & Istita'ah (Ease, Universal Inclusion & Accessibility)**:
   - The platform must facilitate participation for all individuals regardless of technical literacy, visual impairment, motor limitations, or screen size. Dense multi-track grids must provide accessible fallbacks (List and Compact views) with full WAI-ARIA keyboard roving tabindex support.
4. **Hifz al-Khasiyyah (Privacy & Dignity of Personal Routine)**:
   - An attendee curating a personal schedule (*My Schedule* / bookmarks) must be able to do so locally in their browser without being forced into an authentication wall or tracking regime. Synchronized server persistence is strictly opt-in upon explicit authentication.

### 2.2 Key Stakeholders

- **Attendees (Participants & Families)**: Require immediate clarity on what is happening, when, and where; need clear prayer breaks; desire responsive phone browsing while moving across physical venues.
- **Event Organizers & Track Curators**: Need air-traffic-control visibility over room utilization, track clustering, and session readiness without clunky multi-page navigation.
- **Speakers & Workshop Leaders**: Expect accurate session time slots, room capacities, and prompt conflict detection.
- **Platform Operators**: Require performant, low-latency API projections that do not execute N+1 database queries under peak event check-in traffic.

---

## 3. Findings & Mitigations (I-VSD Ledger)

| ID | Domain / Ethical Concern | Risk Description | Architectural Mitigation | Status |
|---|---|---|---|---|
| `IVSD-F001` | **Ibadah Synchronization** | Prayer breaks obscured or buried inside dense secular session tracks; attendees missing congregational prayer due to lack of prominence. | Dedicate distinct visual badges, icons, and non-overlapping time spans for `ScheduleItemKindEnum.Prayer`; ensure high-contrast styling across all 5 view modes. | Mapped to Scenario 3.1 & Phase 2 |
| `IVSD-F002` | **Amanah & Transparency** | Confusing double-rendering (as seen in current `EventDetail.razor` rendering both `EventSessionManager` and `AgendaMillerColumns`) or missing room labels causing attendee venue disorientation. | Replace fragmented conditional rendering with a single unified `EventScheduleContainer` powered by `EventAgendaProjectionDto` with explicit room, venue, and track disclosures. | Mapped to Scenario 3.2 & Phase 1 |
| `IVSD-F003` | **Yusr & Accessibility** | Dense CSS Grid timetables trapping keyboard users in infinite tab stops, or failing users on mobile screens with horizontal scroll fatigue. | Implement WAI-ARIA Grid with roving tabindex (single tab stop, 2D arrow navigation); provide responsive defaults (phone -> List view); offer accessible native `<table>` Compact view. | Mapped to Scenario 3.3 & Phase 4 |
| `IVSD-F004` | **Hifz al-Khasiyyah (Privacy)** | Forcing attendees to create accounts or authenticate just to star/bookmark sessions they want to attend. | Local-first bookmarking storing starred session IDs in browser `localStorage` instantly with zero tracking; optional server sync only when user explicitly signs in. | Mapped to Scenario 3.4 & Phase 3 |
| `IVSD-F005` | **Waqt & Conflict Mitigation** | Attendees inadvertently bookmarking overlapping sessions without notice, leading to missed sessions or last-minute confusion. | Real-time soft conflict detection alerting attendees with amber visual cues (`⚠️ Time Conflict: 2 overlapping sessions`) while preserving attendee autonomy to keep alternatives. | Mapped to Scenario 3.5 & Phase 3 |

---

## 4. Provider-Controlled Decisions & Trade-off Analysis

### 4.1 Soft vs. Hard Conflict Enforcement for Personal Agendas
- **Alternative A (Hard Block)**: Prevent attendees from starring more than one session in the same time slot.
- **Alternative B (Soft Warning - Selected)**: Permit overlapping bookmarks but display a clear, respectful warning badge (`⚠️ Overlap`).
- **Ethical Rationale**: Attendees at multi-track conferences frequently bookmark 2 or 3 parallel talks to decide in the moment based on room occupancy, mood, or companion preferences. Restricting their choice imposes paternalistic control and creates user frustration.

### 4.2 Local-First Storage vs. Compulsory Account Creation
- **Alternative A (Mandatory Auth)**: Require account login before enabling session favorites.
- **Alternative B (Local-First Anonymous - Selected)**: Store favorites in `localStorage` immediately. Offer cloud sync if authenticated.
- **Ethical Rationale**: Respects user privacy (*Hifz al-Khasiyyah*) and eliminates unnecessary data harvesting for casual event attendees who just want to navigate the day.

---

## 5. Escalation Boundaries & Refresh Triggers

- **Scholarly / Jurisprudential Escalation**: Not required for schedule presentation UI; prayer calculation methods and times are already established in the domain layer (`ScheduleTimeZoneResolver` and `SessionEndTimeType.RelativeToPrayer`).
- **Refresh Triggers**:
  1. Introduction of paid workshop reservation locking or seat allocation caps that override personal schedule bookmarking.
  2. Integration of biometric, badge-scanning, or real-time geolocation attendance tracking in venue rooms.

# Plan: Frontend UI Transformation to World-Class LegalTech Standard (Harvey / Linear / Stripe Executive)

## Context & Objectives
The user requested a full, end-to-end upgrade of the frontend to world-class standards ("Nível mundial"). The current frontend has high-value features (DJEN, DataJud, Calculadora Forense, Kanban, Teses, OAB Scraper) but suffers from "AI-prototype aesthetic":
1. Overcrowded rainbow pill header (8 conflicting colored buttons).
2. Cognitive overload and lack of whitespace in the hero area.
3. Fragmented search tabs instead of an intelligent, unified Omnibox.
4. Harsh, unstyled error banners and monolithic layout.
5. Lack of smooth micro-interactions, cohesive typography, and high-density executive information design.

## Target Architecture & Design System
- **Design Tokens:** Deep Slate 950 (`#030712` / `#080c14`), surface `#0e1422`, elevated `#141c30`, border `rgba(255, 255, 255, 0.08)`, subtle hover `rgba(255, 255, 255, 0.03)`.
- **Typography:** `Plus Jakarta Sans` for UI, `JetBrains Mono` for CNJ, OAB, and dates.
- **Top Bar:** Sleek, unified executive navbar with Monogram, Tenant Switcher dropdown, integrated navigation tabs, and system status indicator.
- **Unified Omnibox:** Single intelligent search input that auto-detects CNJ vs OAB vs Lawyer/Party name without requiring mode switching.
- **Refined Error & Empty States:** Subtle amber/slate diagnosis cards with actionable recovery buttons, replacing harsh red banners.
- **Executive Dossier Layout:** Polished Process Master Card, responsive tabs, high-density metadata cards, and professional timeline/document viewers.
- **OAB Portfolio Showcase:** High-impact Lawyer Profile card with court distribution pills (TJRJ, TRT1, TRF2), stats breakdown, and 1-click case inspection.

## Implementation Phases
- **Phase 1:** Design System & CSS Overhaul (`styles.scss` & `app.scss`) — Refined palette, elevation, border-radius, typography, and card tokens.
- **Phase 2:** Executive Header & Navigation Bar (`app.html` & `app.scss`) — Reorganize into clean navbar with Tenant dropdown and consolidated navigation.
- **Phase 3:** Unified Spotlight Omnibox (`app.html`, `app.ts`, `app.scss`) — Single smart search with instant format detection (CNJ / OAB) and elegant sample chips.
- **Phase 4:** Process Master Card & Dossiê Forense (`app.html`, `app.scss`) — Executive layout, clean tabs, elegant metric cards, and refined risk banners.
- **Phase 5:** OAB Portfolio Dashboard (`app.html`, `app.scss`) — Executive lawyer profile, multi-tribunal metrics (TJRJ, TRF2, TRT1), and searchable case table.
- **Phase 6:** Verification & Build — Complete build verification (`npm run build` and `dotnet test`), commit, and push for GitHub Pages deployment.

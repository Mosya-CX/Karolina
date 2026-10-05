import { createContext } from './core/context.js';
import { api } from './core/api.js';
import { createUI } from './ui/primitives.js';
import { createMarkdown } from './ui/markdown.js';
import { registerNavigation } from './features/navigation.js';
import { registerDocuments } from './features/documents.js';
import { registerChat } from './features/chat.js';
import { registerProviders } from './features/providers.js';
import { registerReview } from './review.js';
import { registerWorkspace } from './workspace.js';
import { registerTools } from './tools.js';
import { registerProjectGraph } from './project-graph.js';
import { registerSettings } from './settings.js';
import { createAppearance } from './appearance/controller.js';
import { createEffects } from './effects/controller.js';
import { createMascot } from './ui/mascot.js';
import { createComponents } from './ui/components.js';
import { createPanelLayout } from './ui/panel-layout.js';
import { createChoiceMenus } from './ui/choice-menu.js';
import { registerMarkdownZoom } from './ui/application-zoom.js';
const context = createContext();
context.api = api;
context.ui = createUI(() => context.actions.updateControls?.());
context.ui.markdown = createMarkdown(() => context.state.docs);
for (const register of[registerNavigation, registerDocuments, registerChat, registerProviders, registerReview, registerWorkspace, registerTools, registerProjectGraph, registerSettings]) register(context);
const components = createComponents(context.ui);
const choices = createChoiceMenus();
context.actions.refreshChoices = choices.sync;
const disposeZoom = registerMarkdownZoom(context.ui.toast);
const layout = createPanelLayout(context);
const effects = createEffects(context.ui.toast);
const mascot = createMascot(context.ui);
const appearance = createAppearance({ api, ui: context.ui, components, effects, mascot });
context.actions.appearance = appearance;
context.actions.mountAppearanceSettings = appearance.mountSettings;
window.KarolinaMarkdown = context.ui.markdown;
async function boot() {
    try {
        await appearance.initialize();
        await layout.initialize();
        context.state.docs = await api('documents');
        context.actions.renderReferences();
        await context.actions.loadSettings();
        await context.actions.restoreWorkspace();
        await context.actions.refreshState();
        context.actions.autoConnect();
        // Directory warmup must not delay or change the restored/current page.
        const directories = await Promise.allSettled([
            context.actions.refreshReviews(), context.actions.refreshTools()
        ]);
        for (const result of directories) if (result.status === 'rejected') context.ui.toast(result.reason.message);
        const diagnostic = await api('diagnostics');
        for (const request of diagnostic.approvals || []) context.state.approvalQueue.set(JSON.stringify(request.id), request);
        context.actions.renderApprovals();
        context.actions.renderDirectory();
        context.actions.renderSettings();
        context.actions.autoConnect();
        document.documentElement.dataset.ready = 'true';
    } catch (error) {
        context.actions.finishWorkspaceRestore(false);
        document.documentElement.dataset.ready = 'error';
        context.ui.toast(error.message);
    }
    window.addEventListener('focus', () => {
        if (context.state.page === 'review') context.actions.refreshReviews().catch (error => context.ui.toast(error.message));
        context.actions.autoConnect();
    });
    const timer = setInterval(async () => {
        await context.actions.poll();
        mascot.update(context.state.snapshot);
    }, 1000);
    window.addEventListener('pagehide', () => {
        clearInterval(timer);
        effects.dispose();
        components.dispose();
        choices.dispose();
        disposeZoom();
        layout.dispose();
    }, { once: true });
}
boot();

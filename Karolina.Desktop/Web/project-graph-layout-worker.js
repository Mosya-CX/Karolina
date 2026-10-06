import { layoutGraph } from './project-graph-layout.js';
self.onmessage = event => {
    try { self.postMessage({ layout: layoutGraph(event.data) }); }
    catch (error) { self.postMessage({ error: error.message }); }
};

import { cameraFrame } from './project-graph-camera.js';
import { graphCategory } from './project-graph-category.js';

const palette = { code: '#699ee8', type: '#b096e5', scene: '#e4a761', prefab: '#5cbaa4', art: '#da8fac', resource: '#a1adb9', document: '#d9bf70', file: '#89bdcc' };
const rgb = hex => [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255);
const vertex = `
attribute vec3 aPosition; attribute vec4 aColor; attribute float aSize;
uniform vec3 uTarget,uRight,uUp,uBack; uniform vec2 uScale;
uniform float uDistance,uNear,uFar,uFocal,uDpr;
varying vec4 vColor;
void main(){
 vec3 p=aPosition-uTarget;float depth=uDistance-dot(p,uBack);
 float z=(uFar+uNear)/(uFar-uNear)*depth-2.0*uFar*uNear/(uFar-uNear);
 gl_Position=vec4(dot(p,uRight)*uScale.x,dot(p,uUp)*uScale.y,z,depth);
 gl_PointSize=clamp(aSize*2.0*uFocal/max(depth,1.0),2.8,24.0)*uDpr;
 vColor=aColor;
}`;
const nodeFragment = `precision mediump float; varying vec4 vColor;
void main(){vec2 xy=gl_PointCoord*2.0-1.0;float r=dot(xy,xy);if(r>1.0)discard;
 vec3 normal=vec3(xy.x,-xy.y,sqrt(1.0-r));float light=max(0.0,dot(normal,normalize(vec3(-.4,.5,1.0))));
 float shine=pow(max(0.0,dot(normal,normalize(vec3(-.3,.3,1.0)))),24.0);
 gl_FragColor=vec4(vColor.rgb*(.35+.65*light)+.22*shine,vColor.a);}`;
const edgeFragment = `precision mediump float; varying vec4 vColor;void main(){gl_FragColor=vColor;}`;

// 仅拥有图谱 GPU 资源；相机、布局、详情与数据服务由其它模块负责。
export function createGraphRenderer(canvas, graph, layout) {
    const gl = canvas.getContext('webgl', { antialias: true, alpha: true, depth: true, premultipliedAlpha: false });
    if (!gl) throw new Error('当前环境无法创建WebGL三维视图');
    const programs = [], shaders = [], buffers = [];
    const dispose = () => { buffers.forEach(b => gl.deleteBuffer(b)); programs.forEach(p => gl.deleteProgram(p)); shaders.forEach(s => gl.deleteShader(s)); };
    function program(fragment) {
        const p = gl.createProgram(); programs.push(p);
        for (const [type, source] of [[gl.VERTEX_SHADER, vertex], [gl.FRAGMENT_SHADER, fragment]]) {
            const s = gl.createShader(type); shaders.push(s); gl.shaderSource(s, source); gl.compileShader(s);
            if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error('三维着色器编译失败：' + gl.getShaderInfoLog(s));
            gl.attachShader(p, s);
        }
        gl.linkProgram(p); if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error('三维着色器链接失败：' + gl.getProgramInfoLog(p));
        const attributes = Object.fromEntries(['aPosition','aColor','aSize'].map(name => [name, gl.getAttribLocation(p, name)]));
        const uniforms = Object.fromEntries(['uTarget','uRight','uUp','uBack','uScale','uDistance','uNear','uFar','uFocal','uDpr'].map(name => [name, gl.getUniformLocation(p, name)]));
        return { program: p, attributes, uniforms };
    }
    function buffer(values, usage = gl.STATIC_DRAW) { const b = gl.createBuffer(); buffers.push(b); gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.bufferData(gl.ARRAY_BUFFER, values, usage); return b; }
    try {
        const nodeProgram = program(nodeFragment), edgeProgram = program(edgeFragment);
        const nodes = graph.nodes, nodePositions = new Float32Array(nodes.length * 3);
        nodes.forEach((n, i) => { const p = layout.positions.get(n.id); nodePositions.set([p.x, p.y, p.z], i * 3); });
        const edges = graph.edges.filter(e => layout.positions.has(e.from) && layout.positions.has(e.to));
        const edgePositions = new Float32Array(edges.length * 6);
        edges.forEach((e, i) => { const a = layout.positions.get(e.from), b = layout.positions.get(e.to); edgePositions.set([a.x,a.y,a.z,b.x,b.y,b.z], i * 6); });
        const nodePosition = buffer(nodePositions), edgePosition = buffer(edgePositions);
        const nodeColors = new Float32Array(nodes.length * 4), nodeSizes = new Float32Array(nodes.length), edgeColors = new Float32Array(edges.length * 8);
        const nodeColor = buffer(nodeColors, gl.DYNAMIC_DRAW), nodeSize = buffer(nodeSizes, gl.DYNAMIC_DRAW), edgeColor = buffer(edgeColors, gl.DYNAMIC_DRAW);
        let lastStyle = null;
        function style(selected, neighbors, light) {
            const key = `${selected}|${light}`; if (key === lastStyle) return; lastStyle = key;
            nodes.forEach((n, i) => {
                const color = rgb(palette[graphCategory(n)]), faded = selected && !neighbors.has(n.id);
                nodeColors.set([...color.map(v => v * (faded ? .36 : 1)), 1], i * 4); nodeSizes[i] = n.id === selected ? 12 : 7;
            });
            edges.forEach((e, i) => {
                const active = e.from === selected || e.to === selected, same = layout.groupById.get(e.from) === layout.groupById.get(e.to);
                const value = active ? [light ? .35 : .72, .5, .95, .96] : [light ? .25 : .65, light ? .28 : .7, light ? .35 : .8, selected ? .025 : same ? .16 : .045];
                edgeColors.set([...value, ...value], i * 8);
            });
            for (const [b, values] of [[nodeColor,nodeColors],[nodeSize,nodeSizes],[edgeColor,edgeColors]]) { gl.bindBuffer(gl.ARRAY_BUFFER, b); gl.bufferSubData(gl.ARRAY_BUFFER, 0, values); }
        }
        const enabled = new Set();
        function bind(p, camera, width, height, dpr) {
            for (const index of enabled) gl.disableVertexAttribArray(index); enabled.clear();
            gl.useProgram(p.program); const f = cameraFrame(camera), focal = height / (2 * Math.tan(Math.PI / 8)), u = p.uniforms;
            for (const [name, value] of [['uTarget',camera.target],['uRight',f.right],['uUp',f.up],['uBack',f.back]]) gl.uniform3f(u[name], value.x, value.y, value.z);
            gl.uniform2f(u.uScale, 2 * focal / width, 2 * focal / height); gl.uniform1f(u.uDistance, camera.distance);
            gl.uniform1f(u.uNear, 1); gl.uniform1f(u.uFar, camera.distance + layout.radius * 4 + 100); gl.uniform1f(u.uFocal, focal); gl.uniform1f(u.uDpr, dpr);
        }
        function attribute(p, name, b, count) { const index = p.attributes[name]; if (index < 0) return; gl.bindBuffer(gl.ARRAY_BUFFER,b); gl.enableVertexAttribArray(index); enabled.add(index); gl.vertexAttribPointer(index,count,gl.FLOAT,false,0,0); }
        return {
            draw(camera, width, height, dpr, selected, neighbors, light) {
                style(selected, neighbors, light); gl.viewport(0,0,canvas.width,canvas.height);
                gl.clearColor(0,0,0,0); gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT); gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LEQUAL); gl.depthMask(true); gl.disable(gl.BLEND);
                bind(nodeProgram,camera,width,height,dpr); attribute(nodeProgram,'aPosition',nodePosition,3); attribute(nodeProgram,'aColor',nodeColor,4); attribute(nodeProgram,'aSize',nodeSize,1);
                gl.drawArrays(gl.POINTS,0,nodes.length);
                bind(edgeProgram,camera,width,height,dpr); attribute(edgeProgram,'aPosition',edgePosition,3); attribute(edgeProgram,'aColor',edgeColor,4);
                // 线段不使用点大小；对应属性可能被GL优化掉。
                if (edgeProgram.attributes.aSize >= 0) { gl.disableVertexAttribArray(edgeProgram.attributes.aSize); gl.vertexAttrib1f(edgeProgram.attributes.aSize,1); }
                gl.depthMask(false); gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA,gl.ONE_MINUS_SRC_ALPHA); gl.drawArrays(gl.LINES,0,edges.length*2); gl.depthMask(true);
                const error = gl.getError(); if (error !== gl.NO_ERROR) throw new Error('三维图渲染失败，GL错误码：' + error);
            }, dispose,
            stats: { renderer: 'WebGL', dimensions: 3, nodeVertices: nodes.length, edgeVertices: edges.length * 2, depthTest: true }
        };
    } catch (error) { dispose(); throw error; }
}

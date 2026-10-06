import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
async function module(name){const source=await readFile(new URL('../Karolina.Desktop/Web/'+name,import.meta.url),'utf8');return import('data:text/javascript;base64,'+Buffer.from(source).toString('base64'));}
const {layoutGraph}=await module('project-graph-layout.js');
const {graphCategory}=await module('project-graph-category.js');
const {cameraFrame,projectPoint,fitCamera,panCamera,rotateCamera,zoomCamera,pickNode,dot}=await module('project-graph-camera.js');
const distance=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);

test('三维全图包含孤立节点、声明和循环，坐标有限且具有深度',()=>{
 const graph={nodes:[{id:'a',kind:'代码文件',path:'Assets/A.cs'},{id:'t',kind:'C# 类型',path:'Assets/A.cs'},{id:'b',kind:'代码文件',path:'Tools/B.cs'},{id:'alone',path:'README.md'}],edges:[{from:'a',to:'t',relation:'声明'},{from:'t',to:'b',relation:'类型引用'},{from:'b',to:'a',relation:'类型引用'}]};
 const l=layoutGraph(graph);assert.equal(l.positions.size,4);assert.equal(l.dimensions,3);assert.equal(l.method,'relations-3d');
 for(const p of l.positions.values()){assert.ok(Number.isFinite(p.x)&&Number.isFinite(p.y)&&Number.isFinite(p.z));assert.ok(Math.hypot(p.x,p.y,p.z)<l.radius);}
 assert.ok(new Set([...l.positions.values()].map(p=>p.z)).size>2);assert.equal(l.regions,undefined);assert.ok(distance(l.positions.get('a'),l.positions.get('t'))<30);
});
test('数千节点不截断，翻转输入仍得到相同三维结果',()=>{
 const graph={nodes:Array.from({length:4000},(_,i)=>({id:`node:${i}`,path:`Any${i%40}/File${i}.cs`})),edges:[]};
 const l=layoutGraph(graph),again=layoutGraph({nodes:[...graph.nodes].reverse(),edges:[]});assert.equal(l.positions.size,4000);assert.equal(l.groups,0);
 assert.deepEqual([...l.positions],[...again.positions]);
});
test('修改目录、路径和标题不改变空间分组和位置',()=>{
 const nodes=Array.from({length:12},(_,i)=>({id:`n${i}`,kind:'代码文件',title:'File'+i,path:`Dir${i}/N${i}.cs`}));
 const edges=Array.from({length:10},(_,i)=>({from:`n${i}`,to:`n${i+1}`,relation:'Unity GUID 引用'}));
 const a=layoutGraph({nodes,edges}),b=layoutGraph({nodes:nodes.map(n=>({...n,title:'Changed',path:'SameFolder/Everything.cs',folder:'ChangedFolder'})),edges});
 assert.deepEqual([...a.positions],[...b.positions]);assert.deepEqual([...a.groupById],[...b.groupById]);
});
test('没有声明边时同一路径不会使节点被合并',()=>{
 const l=layoutGraph({nodes:[{id:'a',kind:'代码文件',path:'Same.cs'},{id:'t',kind:'C# 类型',path:'Same.cs'}],edges:[]});assert.equal(l.positions.size,2);assert.ok(distance(l.positions.get('a'),l.positions.get('t'))>30);
});
test('跨目录密集引用靠近，弱联系的两群保持分开',()=>{
 const nodes=Array.from({length:8},(_,i)=>({id:`f${i}`,kind:'代码文件',path:`Folder${i}/File${i}.cs`})),edges=[];
 for(let start=0;start<8;start+=4)for(let a=start;a<start+4;a++)for(let b=a+1;b<start+4;b++)edges.push({from:`f${a}`,to:`f${b}`,relation:'Unity GUID 引用'});
 edges.push({from:'f3',to:'f4',relation:'类型引用'});const l=layoutGraph({nodes,edges});
 for(const id of ['f1','f2','f3'])assert.equal(l.groupById.get(id),l.groupById.get('f0'));
 assert.notEqual(l.groupById.get('f0'),l.groupById.get('f4'));assert.ok(distance(l.positions.get('f0'),l.positions.get('f1'))<distance(l.positions.get('f0'),l.positions.get('f4')));
 assert.deepEqual([...l.positions],[...layoutGraph({nodes:[...nodes].reverse(),edges:[...edges].reverse()}).positions]);
});
test('空图、自引用和断开图有效，节点不重叠',()=>{
 assert.equal(layoutGraph({nodes:[],edges:[]}).positions.size,0);
 const nodes=Array.from({length:100},(_,i)=>({id:`n${i}`,kind:'代码文件',path:`Group${i%4}/N${i}.cs`})),edges=[];
 for(let i=0;i<90;i++)edges.push({from:`n${i}`,to:`n${(i+1)%90}`,relation:'类型引用'});edges.push({from:'n0',to:'n0',relation:'类型引用'});
 const l=layoutGraph({nodes,edges}),p=[...l.positions.values()];assert.equal(p.length,100);
 for(let i=0;i<p.length;i++)for(let j=i+1;j<p.length;j++)assert.ok(distance(p[i],p[j])>12);
});
test('透视具有近大远小，旋转改变投影，平移移动视野中心',()=>{
 const c={yaw:0,pitch:0,distance:500,target:{x:0,y:0,z:0}},near=projectPoint({x:10,y:0,z:100},c,800,600),far=projectPoint({x:10,y:0,z:-100},c,800,600);
 assert.ok(near.x-400>far.x-400);assert.ok(near.depth<far.depth);assert.equal(projectPoint({x:0,y:0,z:600},c,800,600).visible,false);
 assert.equal(projectPoint({x:0,y:0,z:-1000},{...c,far:800},800,600).visible,false);
 const before=projectPoint({x:100,y:30,z:50},c,800,600);rotateCamera(c,30,20);const after=projectPoint({x:100,y:30,z:50},c,800,600);assert.notEqual(after.x,before.x);
 const f=cameraFrame(c);assert.ok(Math.abs(dot(f.right,f.up))<1e-10&&Math.abs(dot(f.right,f.back))<1e-10);panCamera(c,30,10,600);assert.ok(Math.hypot(c.target.x,c.target.y,c.target.z)>0);
 zoomCamera(c,1.18,1000);assert.ok(c.distance<500);
});
test('适应全图可容纳三维球体，重叠点击只选前方可见节点',()=>{
 const c={yaw:.7,pitch:.3,distance:10,target:{x:10,y:0,z:0}};fitCamera(c,1000,420,300);
 for(const p of [{x:1000,y:0,z:0},{x:-1000,y:0,z:0},{x:0,y:1000,z:0},{x:0,y:0,z:1000}]){const v=projectPoint(p,c,420,300);assert.ok(v.visible&&v.x>=0&&v.x<=420&&v.y>=0&&v.y<=300);}
 assert.equal(pickNode([{id:'back',x:50,y:50,depth:200,size:12,visible:true},{id:'front',x:51,y:50,depth:100,size:12,visible:true},{id:'clipped',x:50,y:50,depth:-1,size:12,visible:false}],50,50),'front');
 assert.equal(pickNode([],0,0),null);
});
test('代码、类型和Unity资源分类保留',()=>{
 for(const [node,category]of [[{kind:'代码文件',path:'A.lua'},'code'],[{kind:'C# 类型'},'type'],[{kind:'工程文档'},'document'],[{kind:'工程文件'},'file'],[{kind:'Unity资源',path:'A.prefab'},'prefab'],[{kind:'Unity资源',path:'A.anim'},'art']])assert.equal(graphCategory(node),category);
});

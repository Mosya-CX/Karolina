import test from 'node:test';
import assert from 'node:assert/strict';
import{createFeedback,reduceFeedback,summaryText}from'../Karolina.Desktop/Web/ui/chat-progress.js';
const start={method:'turn/started',params:{threadId:'chat-a',turn:{id:'turn-a'}}};
test('状态由真实事件更新，原始CLI和私有推理事件不进入显示模型',()=>{
 const s=createFeedback();reduceFeedback(s,start);
 for(const e of [{method:'item/started',params:{item:{type:'commandExecution',command:'SECRET_CLI'}}},{method:'item/reasoning/textDelta',params:{delta:'PRIVATE_REASONING'}}])assert.equal(reduceFeedback(s,e),false);
 assert.ok(!JSON.stringify(s).includes('SECRET_CLI'));assert.ok(!JSON.stringify(s).includes('PRIVATE_REASONING'));
 assert.equal(reduceFeedback(s,{method:'karolina/activity',params:{threadId:'chat-a',turnId:'turn-a',stage:'查阅资料',detail:'正在查找项目资料',reasoningItemId:'reason-a',reasoningSummary:'核对本轮功能与验收要求',plan:[{step:'核对资料',status:'in_progress'}]}}),true);
 assert.equal(s.stage,'查阅资料');assert.equal(s.summaries.get('reason-a'),'核对本轮功能与验收要求');assert.equal(s.plan.length,1);
});
test('其它对话和旧轮次活动不能覆盖当前反馈',()=>{
 const s=createFeedback();reduceFeedback(s,start);
 for(const params of [{threadId:'chat-b',turnId:'turn-a'},{threadId:'chat-a',turnId:'turn-old'}])assert.equal(reduceFeedback(s,{method:'karolina/activity',params:{...params,stage:'错误阶段'}}),false);
 assert.equal(s.stage,'理解目标');assert.equal(reduceFeedback(s,{method:'turn/completed',params:{threadId:'chat-a',turn:{id:'turn-old',status:'completed'}}}),false);assert.equal(s.active,true);
});
test('失败、中断、断连及终态后迟到事件保持真实状态',()=>{
 for(const status of ['failed','interrupted','completed']){const s=createFeedback();reduceFeedback(s,start);reduceFeedback(s,{method:'turn/completed',params:{threadId:'chat-a',turn:{id:'turn-a',status}}});assert.equal(s.active,false);assert.equal(s.state,status);assert.equal(reduceFeedback(s,{method:'karolina/activity',params:{threadId:'chat-a',turnId:'turn-a',stage:'继续执行'}}),false);if(status==='completed')assert.ok(s.detail.includes('不代表'));}
 const s=createFeedback();reduceFeedback(s,start);reduceFeedback(s,{method:'karolina/disconnected',params:{}});assert.equal(s.state,'disconnected');assert.equal(s.active,false);
});
test('历史只读取公开summary，不读取私有content',()=>{
 assert.equal(summaryText({summary:['摘要一',{text:'摘要二'}],content:['PRIVATE_REASONING']}),'摘要一\n摘要二');assert.equal(summaryText({content:['PRIVATE_REASONING']}),'');assert.equal(summaryText({summary:null}),'');
});

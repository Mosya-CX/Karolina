const terminalLabels={completed:'已完成',failed:'失败',interrupted:'已停止',disconnected:'连接中断',unknown:'回执未知'};
export const summaryText=item=>(Array.isArray(item?.summary)?item.summary:[]).map(s=>typeof s==='string'?s:s?.text || '').join('\n');
export function createFeedback(){return{threadId:null,turnId:null,active:false,state:'ready',stage:'准备',detail:'',updated:null,started:Date.now(),summaries:new Map(),summaryWarning:'',plan:[]};}
export function reduceFeedback(state,event){
    const p=event.params || {},method=event.method;
    if(p.threadId&&state.threadId&&p.threadId!==state.threadId)return false;
    const turn=p.turnId || p.turn?.id;
    if(turn&&state.turnId&&turn!==state.turnId)return false;
    if(method==='turn/started'){state.threadId=p.threadId;state.turnId=turn;state.active=true;state.state='running';state.stage='理解目标';state.detail='Agent 已接收本轮指令，正在理解目标和参考资料';}
    else if(method==='karolina/activity'){
        if(!state.active)return false;
        state.threadId=p.threadId;state.turnId=turn || state.turnId;state.stage=p.stage;state.detail=p.detail;
        state.plan=p.plan || state.plan;
        if(p.reasoningItemId&&p.reasoningSummary)state.summaries.set(p.reasoningItemId,p.reasoningSummary);
    }else if(method==='turn/completed'){
        state.threadId=p.threadId;state.turnId=turn;state.active=false;state.state=p.turn?.status || 'unknown';state.stage=terminalLabels[state.state] || '轮次已结束';
        state.detail=state.state==='completed'?'本轮已结束，请查看 Agent 结论；执行完成不代表功能验收通过':state.state==='interrupted'?'已停止本轮，已有结果仍保留':p.turn?.error?.message || '本轮未完成，请检查连接或失败原因';
    }else if(method==='karolina/disconnected'){
        if(!state.active)return false;state.active=false;state.state='disconnected';state.stage='连接中断';state.detail='尚未收到本轮完成回执，请检查连接';
    }else return false;
    state.updated=p.at || new Date().toISOString();return true;
}
export function mountChatProgress(container){
    let state=createFeedback(),card=null,stage=null,detail=null,clock=null,plan=null,thinking=null,signature='';
    function ensure(){
        if(card?.isConnected)return;
        card=document.createElement('section');card.className='chat-progress';
        const header=document.createElement('header'),title=document.createElement('strong');title.textContent='本轮进度';stage=document.createElement('span');stage.className='chat-progress-state';stage.setAttribute('role','status');header.append(title,stage);
        detail=document.createElement('p');detail.className='chat-progress-detail';clock=document.createElement('small');clock.className='chat-progress-clock';
        plan=document.createElement('ol');plan.className='chat-progress-plan';thinking=document.createElement('div');thinking.className='chat-progress-thinking';
        card.append(header,detail,clock,plan,thinking);container.append(card);signature='';
    }
    function time(){if(!card?.isConnected)return;const age=Math.max(0,Math.floor((Date.now()-new Date(state.updated || state.started).getTime())/1000)),elapsed=Math.max(0,Math.floor((Date.now()-state.started)/1000));clock.textContent=state.active?`已用时 ${elapsed} 秒 · ${age>15?'等待新的 Agent 回执':`最近反馈 ${age} 秒前`}`:'根据 Agent 的实际回执显示，不代表人工验收';}
    function paint(){
        const atBottom=container.scrollHeight-container.scrollTop-container.clientHeight<100;
        ensure();stage.textContent=state.stage;card.dataset.state=state.state;detail.textContent=state.detail;
        const next=JSON.stringify([state.plan,[...state.summaries],state.summaryWarning]);
        if(next!==signature){signature=next;plan.replaceChildren();for(const step of state.plan){const li=document.createElement('li');li.dataset.status=step.status;li.textContent=`${({pending:'待进行',in_progress:'进行中',completed:'已完成'})[step.status] || '状态待确认'} · ${step.step}`;plan.append(li);}plan.hidden=!state.plan.length;
            thinking.replaceChildren();const heading=document.createElement('strong');heading.textContent='Agent 思考摘要';thinking.append(heading);
            if(state.summaries.size){for(const text of state.summaries.values()){const p=document.createElement('p');p.textContent=text;thinking.append(p);}}
            else{const p=document.createElement('p');p.className='chat-progress-empty';p.textContent=state.active?'等待模型提供公开摘要；部分模型或档位可能不提供':'此轮未提供公开思考摘要';thinking.append(p);}
            if(state.summaryWarning){const p=document.createElement('p');p.className='chat-progress-empty';p.textContent=state.summaryWarning;thinking.append(p);}
        }time();if(atBottom)container.scrollTop=container.scrollHeight;
    }
    function begin({threadId,mode,model,effort,references}){state=createFeedback();state.threadId=threadId;state.active=true;state.state='preparing';state.stage='准备发送';state.detail=`正在准备${({ 'discuss-requirement':'需求讨论','formulate-plan':'计划制定','execute-task':'任务执行'})[mode] || '本轮对话'} · ${model} / ${effort}${references.length?' · 参考：'+references.join('、'):''}`;card=null;paint();}
    function accepted(threadId,turnId){state.threadId=threadId;state.turnId=turnId;state.active=true;state.state='running';if(state.stage==='准备发送'){state.stage='等待反馈';state.detail='指令已发送，等待 Agent 的下一条回执';}paint();}
    function failed(message){state.active=false;state.state='failed';state.stage='未能发送';state.detail=message;paint();}
    function receive(event){if(reduceFeedback(state,event))paint();}
    function restoreSummaries(threadId,turnId,items){
        if(state.threadId!==threadId||state.turnId!==turnId)return false;
        state.summaryWarning='';for(const item of items || [])if(item.type==='reasoning'&&summaryText(item))state.summaries.set(item.id,summaryText(item));paint();return true;
    }
    function summaryUnavailable(threadId,turnId,message){if(state.threadId!==threadId||state.turnId!==turnId)return;state.summaryWarning='历史摘要读取失败，已收到的实时反馈仍保留：'+message;paint();}
    function reset(){state=createFeedback();card=null;}
    function history(threadId,turn){
        state=createFeedback();state.threadId=threadId;state.turnId=turn.id;state.state=turn.status || 'unknown';state.active=turn.status==='inProgress';state.stage=state.active?'正在处理':terminalLabels[state.state] || '历史轮次';state.detail=state.active?'等待本轮的新回执':'本轮已保存，可查看 Agent 的结论和公开摘要';
        for(const item of turn.items || [])if(item.type==='reasoning'&&summaryText(item))state.summaries.set(item.id,summaryText(item));card=null;paint();
    }
    function sync(snapshot,threadId){const run=snapshot.currentRun;if(!run || !threadId || run.threadId!==threadId || state.turnId&&run.turnId&&state.turnId!==run.turnId)return;
        if(!card?.isConnected && !snapshot.busy)return;
        if(state.state==='preparing')return;
        state.threadId=threadId;state.turnId=run.turnId || state.turnId;state.started=new Date(run.started || state.started).getTime();
        if(run.reasoningItemId&&run.reasoningSummary)state.summaries.set(run.reasoningItemId,run.reasoningSummary);state.plan=run.activityPlan || state.plan;
        if(snapshot.busy){state.active=true;state.state=run.state;state.stage=run.progressStage==='运行命令'?'执行操作':run.progressStage;state.detail=run.progressStage==='运行命令'?'正在处理项目资料':run.progressMessage;state.updated=run.progressUpdated;}
        else if(state.active){state.active=false;state.state=run.state;state.stage=terminalLabels[run.state] || '轮次已结束';state.detail=run.error || (run.state==='completed'?'本轮已结束，请查看 Agent 结论；执行完成不代表功能验收通过':'本轮已结束，请核对实际结果');}paint();
    }
    const timer=setInterval(time,1000);window.addEventListener('unload',()=>clearInterval(timer),{once:true});
    return{begin,accepted,failed,receive,restoreSummaries,summaryUnavailable,reset,history,sync};
}

// 覆盖模块导入和控制器注册阶段；启动异常也要在页面上明确显示。
import('./app.js').catch(error => {
    document.documentElement.dataset.ready = 'error';
    const status = document.getElementById('taskStatus');
    if (status) status.textContent = '工作台初始化失败';
    const message = document.getElementById('toast');
    if (message) {
        message.textContent = '工作台初始化失败：' + error.message;
        message.hidden = false;
    }
    console.error('工作台初始化失败', error);
});

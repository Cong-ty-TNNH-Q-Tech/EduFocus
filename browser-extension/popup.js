const dot = document.getElementById('dot');
const statusText = document.getElementById('status-text');
const detail = document.getElementById('detail');

function render(status) {
  if (!status) {
    dot.className = 'dot off';
    statusText.textContent = 'Không đọc được trạng thái';
    detail.textContent = 'Hãy mở lại tiện ích hoặc kiểm tra EduFocus.';
    return;
  }

  if (status.connected && status.active) {
    dot.className = 'dot on';
    statusText.textContent = 'Đang bảo vệ';
    detail.textContent = `${status.ruleCount} rule đang được áp dụng cho trình duyệt này.`;
    return;
  }

  if (status.connected) {
    dot.className = 'dot warn';
    statusText.textContent = 'Đã kết nối · Chưa có rule chặn';
    detail.textContent = 'EduFocus đang chạy nhưng chưa có rule chặn URL/path nào được bật.';
    return;
  }

  dot.className = 'dot off';
  statusText.textContent = 'Chưa kết nối EduFocus';
  detail.textContent = 'EduFocus chưa chạy hoặc bảo vệ đang tắt, nên trình duyệt không bị chặn.';
}

chrome.runtime.sendMessage({ type: 'status' }, (response) => {
  if (chrome.runtime.lastError) {
    render(null);
    return;
  }

  render(response);
});

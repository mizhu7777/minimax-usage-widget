-- P0-2 修复:这个 Lua 现在真正被 Rainmeter 加载,提供 Update() 主回调
-- 与 freshenIni 协同(供其他 measure 复用)。
-- 关键 Rainmeter 约束:
--   1) Script measure 必须实现 Update(),缺失则 measure 报错无值
--   2) measure option 通过 SELF:GetOption('name', 'default') 读取
--   3) 不要使用 SKIN:GetMeasureOption (此 API 不存在)

function Initialize()
  project = SKIN:GetVariable('ProjectDir')
  -- StaleAfterSec 在 ini 中用 MeasureOption=StaleAfterSec=180 传入
  local opt = SELF:GetOption('StaleAfterSec', '180')
  STALE_AFTER = tonumber(opt) or 180
end

local function read_first_line(path)
  local file = io.open(path, 'r')
  if not file then return nil end
  local value = file:read('*l')
  file:close()
  if value == nil then return nil end
  value = value:gsub('[\r\n]', '')
  return value
end

-- 内部:读 epoch 并计算新鲜度;返回 4 种状态字符串之一
local function computeStatus()
  local raw = read_first_line(project .. '\\.cache\\last-success-epoch.txt')
  if not raw or raw == '' then
    return '● 未知'
  end
  local epoch = tonumber(raw)
  if not epoch then
    return '● 未知'
  end
  local age = os.time() - epoch
  if age < 0 then age = 0 end

  -- 读 status.txt 看脚本自己写的状态
  local script_status = read_first_line(project .. '\\.cache\\status.txt') or ''

  -- 优先级:已过期 > 错误 > 稳定
  if age > STALE_AFTER then
    return '● 已过期'
  end
  if script_status == '● 错误' then
    return '● 错误'
  end
  if script_status == '● 稳定' then
    return '● 稳定'
  end
  if script_status ~= '' then
    return script_status
  end
  return '● 未知'
end

-- Rainmeter Script measure 必需的主回调
-- P2-12 修复:按状态给 [MeterStatusBadge] 报警变色 —— 稳定浅蓝 / 已过期橙 / 错误红 / 未知灰
local STATUS_COLORS = {
  ['● 稳定']   = '79,195,247,255',
  ['● 已过期'] = '245,158,11,255',
  ['● 错误']   = '239,68,68,255',
  ['● 未知']   = '158,166,180,255',
}

function Update()
  local status = computeStatus()
  SKIN:Bang('!SetOption', 'MeterStatusBadge', 'FontColor', STATUS_COLORS[status] or '79,195,247,255')
  SKIN:Bang('!UpdateMeter', 'MeterStatusBadge')
  SKIN:Bang('!Redraw')
  return status
end
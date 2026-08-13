function Initialize()
  project = SKIN:GetVariable('ProjectDir')
end

local function read_first_line(path)
  local file = io.open(path, 'r')
  if not file then return '' end
  local value = file:read('*l')
  file:close()
  return value or ''
end

function GetStringValue()
  return read_first_line(project .. '\\.cache\\status.txt')
end

function GetReset1()
  return read_first_line(project .. '\\.cache\\reset1.txt')
end

function GetReset2()
  return read_first_line(project .. '\\.cache\\reset2.txt')
end

function GetUpdated()
  return read_first_line(project .. '\\.cache\\updated.txt')
end
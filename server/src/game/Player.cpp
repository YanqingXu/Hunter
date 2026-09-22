// 实现 Player 的属性边界，玩法决策留在对应 Lua 模块。
#include "game/Player.h"
#include "common/Types.h"

namespace hunter
{
Str Player::get_player_id() const
{
    return std::to_string(player_id);
}

i64 Player::get_reserve() const
{
    return reserve;
}

void Player::set_reserve(i64 value)
{
    access->write();
    range(value, 0, 100000);
    reserve = static_cast<i32>(value);
}

i64 Player::get_move_x() const
{
    return move_x;
}

void Player::set_move_x(i64 value)
{
    access->write();
    range(value, -1, 1);
    move_x = static_cast<i32>(value);
}

i64 Player::get_aim_x() const
{
    return aim_x;
}

void Player::set_aim_x(i64 value)
{
    access->write();
    range(value, -1000, 1000);
    aim_x = static_cast<i32>(value);
}

i64 Player::get_aim_y() const
{
    return aim_y;
}

void Player::set_aim_y(i64 value)
{
    access->write();
    range(value, -1000, 1000);
    aim_y = static_cast<i32>(value);
}

bool Player::get_jump() const
{
    return jump;
}

void Player::set_jump(bool value)
{
    access->write();
    jump = value;
}

bool Player::get_fire() const
{
    return fire;
}

void Player::set_fire(bool value)
{
    access->write();
    fire = value;
}

bool Player::get_fire_once() const
{
    return fire_once;
}

void Player::set_fire_once(bool value)
{
    access->write();
    fire_once = value;
}

bool Player::get_reload() const
{
    return reload;
}

void Player::set_reload(bool value)
{
    access->write();
    reload = value;
}
}

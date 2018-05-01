#include "macros.hpp"
/*
    Community Lib - CLib

    Author: joko // Jonas

    Description:
    Description

    Parameter(s):
    0: Uniqe Idinifier <String>
    1: ClassName/ConfigPath/SimpleObjectStructure <String, Config, Array>
    2: Position <Array>
    3: Rotation <Array>
    4: Ignored Object <Object>
    5: Ignored Object <Object>

    Returns:
    None
*/
params [["_uid", "", [""]], "_input", "_pos", "_dir", ["_ignoreObj1", objNull], ["_ignoreObj2", objNull]];

if !(isServer) then {
    [QGVAR(createSimpleObjectComp), _this] call CFUNC(serverEvent);
};
if (_uid isEqualTo "") exitWith {
    LOG("ERROR: UID Name is not Valid");
};

if !(isNil {GVAR(compNamespace) getVariable _uid}) then {
    LOG("Warning: the UID is already in Use");
};

switch (typeName _input) do {
    case "STRING": {
        GVAR(namespace) getVariable _input
    };
    case "CONFIG": {
        if (!isServer) then {
            LOG("Warning: you Try to Load a Config form a Client that is Not the Server");
        };
        private _temp = GVAR(namespace) getVariable (configName _input);
        if (isNil "_temp") then {
            _input call CFUNC(readSimpleObjectComp)
        } else {
            _temp
        };
    };
};

_input params ["_alignOnSurface", "_objects"];

if (isNil "_input" || {_input isEqualTo []}) exitWith {
    LOG("ERROR SimpleObjectComp Dont exist: " + _input);
    nil
};
private _intersections = lineIntersectsSurfaces [
    AGLToASL _pos,
    AGLToASL _pos vectorAdd [0, 0, -100],
    _ignoreObj1,
    _ignoreObj2
];

private _normalVector = (_intersections select 0) select 1;
private _posVectorASL = (_intersections select 0) select 0;

private _originObj = "Land_HelipadEmpty_F" createVehicleLocal [0, 0, 0];
_originObj setPosASL _posVectorASL;

private _xVector = _dir vectorCrossProduct _normalVector;
_dir = _normalVector vectorCrossProduct _xVector;
private _ovUp = [[0, 0, 1], _normalVector] select _alignOnSurface;

_originObj setVectorDirAndUp [_dir, _ovUp];

private _originPosAGL = _originObj modelToWorld [0, 0, 0];
private _originPosASL = AGLToASL _originPosAGL;

private _return = [];
{
    _x params ["_path", "_posOffset", "_dirOffset", "_upOffset", "_hideSelectionArray", "_animateArray", "_setObjectTextureArray"];

    private _obj = objNull;

    _obj = createSimpleObject [_path, AGLToASL (_originObj modelToWorld _posOffset)];
    _obj setVariable [QGVAR(isSimpleObject), true, true];
    _obj setVectorDirAndUp [AGLToASL (_originObj modelToWorld _dirOffset) vectorDiff _originPosASL, AGLToASL (_originObj modelToWorld _upOffset) vectorDiff _originPosASL];

    if (_hideSelectionArray isEqualType [] && {!(_hideSelectionArray isEqualTo [])}) then {
        {
            _obj hideSelection _x;
            nil
        } count _hideSelectionArray;
    };
    if (_animateArray isEqualType [] && {!(_animateArray isEqualTo [])}) then {
        {
            _obj animate _x;
            nil
        } count _animateArray;
    };

    if (_setObjectTextureArray isEqualType [] && {!(_setObjectTextureArray isEqualTo [])}) then {
        {
            _x params ["_type", "_id", "_path"];
            if (_type isEqualTo 1) then {
                _obj setObjectMaterialGlobal [_id, _path];
            } else {
                _obj setObjectTextureGlobal [_id, _path];
            };
            nil
        } count _setObjectTextureArray;
    };
    _return pushBack _obj;
    nil
} count _objects;

deleteVehicle _originObj;

GVAR(compNamespace) setVariable [_uid, _return, true];

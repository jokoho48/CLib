#include "macros.hpp"
/*
    Community Lib - CLib

    Author: joko // Jonas

    Description:
    Checks if a string is localized

    Parameter(s):
    0: Localisation Name <String>

    Returns:
    is Localised <Bool>
*/
params [["_locaName", "STR_CLib_ERROR"]];
private _temp = GVAR(ClientNamespace) getVariable _locaName;

!isNil "_temp";

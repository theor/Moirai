parser grammar Moirai;
options {

  tokenVocab=moirai_lexer;

}
r: (COMMENT | LINE_BREAK)* (action|event|prop_definition|enum_definition|type_definition)+ ;

action: RULE AT? ID SCOPE_OPEN LINE_BREAK effect+ SCOPE_CLOSE LINE_BREAK*;
event: EVENT ID SCOPE_OPEN LINE_BREAK when+ effect+ SCOPE_CLOSE LINE_BREAK*;
when: WHEN (VAR_ID COLON)? expr (COMMA expr)* SPACE* LINE_BREAK+;
effect: (set |  call_assign) SPACE* LINE_BREAK+;

set: SET  path EQ expr;
call_assign : ID (VAR_ID COLON)?  ((expr (COMMA expr)* )) scope?;
call : ID ((expr (COMMA expr)* )) scope?;
scope: SCOPE_OPEN LINE_BREAK* effect* SCOPE_CLOSE LINE_BREAK*;
value: (PAREN_OPEN value PAREN_CLOSE) | call | string | enum_value | TYPE_ID | path | bool | number | NULL;
expr : value (op value)? ;
op : EQ | NEQ ;

type_definition: ENTITY TYPE_ID SCOPE_OPEN LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

prop_definition: PROP ID EQ (ID|TYPE_ID) LINE_BREAK+ ;

enum_definition: ENUM TYPE_ID SCOPE_OPEN LINE_BREAK* TYPE_ID (COMMA TYPE_ID)* LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;

string: STRING ;

bool: TRUE | FALSE;
path : VAR_ID (DOT ID)* | ID;
enum_value: TYPE_ID DOT TYPE_ID ;
number: NUMBER;